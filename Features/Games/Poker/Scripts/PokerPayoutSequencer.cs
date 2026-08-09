using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ChipBatch = PokerChipAnimator.Batch;
using ChipPhase = PokerChipAnimator.Phase;

/// <summary>Owns pot assignment, dealer change and delivery sequencing after a settled hand.</summary>
[GlobalClass]
public partial class PokerPayoutSequencer : Node
{
	[Signal] public delegate void DealerChangeStartedEventHandler();
	[Signal] public delegate void DealerChangeFinishedEventHandler();
	[Signal] public delegate void PayoutFinishedEventHandler();

	public delegate bool SeatPlaces(string playerId, out Basis basis, out Vector3 stack);
	public delegate Vector3 WinnerOffset(string playerId, int denomination, int arrival, PokerChipPile pile);

	private PokerChipAnimator _animator;
	private PokerPresentationProfile _profile;
	private Func<ChipBatch> _acquire;
	private Func<int> _nextSequence;
	private SeatPlaces _seatPlaces;
	private WinnerOffset _winnerOffset;
	private PokerPayoutPlanner.Plan _plan;
	private int _maximumGroups;
	private Vector3 _dealerPoint;

	public bool Started { get; private set; }
	public bool Completed { get; private set; }
	public bool DealerChangeInProgress { get; private set; }
	public bool DealerChangeCompleted { get; private set; }

	public void Configure(PokerChipAnimator animator, PokerPresentationProfile profile,
		int maximumGroups, Func<ChipBatch> acquire, Func<int> nextSequence,
		SeatPlaces seatPlaces, WinnerOffset winnerOffset)
	{
		_animator = animator;
		_profile = profile ?? new PokerPresentationProfile();
		_maximumGroups = Mathf.Max(1, maximumGroups);
		_acquire = acquire;
		_nextSequence = nextSequence;
		_seatPlaces = seatPlaces;
		_winnerOffset = winnerOffset;
	}

	public void Reset(bool authoritativeSettled = false)
	{
		Started = false;
		Completed = authoritativeSettled;
		DealerChangeInProgress = false;
		DealerChangeCompleted = false;
		_plan = null;
	}

	public bool Begin(IReadOnlyDictionary<string, int> awards,
		IReadOnlyList<string> seatOrder, Vector3 dealerPoint)
	{
		var pot = _animator.Batches.Where(batch => batch.Phase == ChipPhase.InPot)
			.OrderByDescending(batch => batch.Amount).ThenBy(batch => batch.Sequence).ToList();
		if (awards == null || awards.Count == 0 || pot.Count == 0)
			return false;

		Started = true;
		Completed = false;
		_dealerPoint = dealerPoint;
		_plan = PokerPayoutPlanner.Create(pot.Select(batch => batch.Amount).ToList(), awards, seatOrder);
		if (_plan.RequiresDealerChange)
			BeginDealerChange(pot);
		else
			StartPayout(pot, _plan.ExistingRecipients);
		return true;
	}

	/// <summary>Called after the chip animator advanced all actors for this frame.</summary>
	public bool Advance()
	{
		if (!Started || Completed)
			return false;
		if (DealerChangeInProgress && !_animator.HasPhase(ChipPhase.ToDealer))
		{
			CompleteDealerChange();
			return true;
		}
		if (!DealerChangeInProgress && !_animator.HasPhase(ChipPhase.ToWinner))
		{
			Completed = true;
			EmitSignal(SignalName.PayoutFinished);
			return true;
		}
		return false;
	}

	private void BeginDealerChange(IReadOnlyList<ChipBatch> pot)
	{
		DealerChangeInProgress = true;
		DealerChangeCompleted = false;
		EmitSignal(SignalName.DealerChangeStarted);
		for (var i = 0; i < pot.Count; i++)
		{
			var batch = pot[i];
			batch.WinnerId = "";
			batch.From = batch.Pile.Position;
			batch.To = _dealerPoint + DealerOffset(i);
			batch.FromBasis = batch.Pile.Basis;
			batch.ToBasis = Basis.Identity;
			batch.Progress = 0.0f;
			batch.Delay = i * _profile.DealerChangeStagger;
			batch.JustStarted = true;
			batch.Phase = ChipPhase.ToDealer;
			batch.Pile.FlightProgress = 0.0f;
			batch.Pile.Spread = 0.0f;
		}
	}

	private void CompleteDealerChange()
	{
		var retired = _animator.Batches.Where(batch => batch.Phase == ChipPhase.AtDealer).ToList();
		var origins = retired.Select(batch => batch.Pile.Position).ToList();
		foreach (var batch in retired)
			_animator.Reset(batch);

		var exact = new List<ChipBatch>();
		var recipients = new List<string>();
		var remainingBudget = _maximumGroups;
		for (var winnerIndex = 0; winnerIndex < _plan.Winners.Count; winnerIndex++)
		{
			var winner = _plan.Winners[winnerIndex];
			var reserve = _plan.Winners.Skip(winnerIndex + 1)
				.Sum(next => next.ExactRuns.Count(run => run.Count > 0));
			var grouped = PokerChipAnimator.GroupRuns(winner.ExactRuns,
				Mathf.Max(winner.ExactRuns.Count, remainingBudget - reserve));
			foreach (var run in grouped)
			{
				var batch = _acquire();
				batch.Amount = run.Value;
				batch.Sequence = _nextSequence();
				batch.WinnerId = winner.PlayerId;
				batch.Duration = _profile.DealerPayoutSeconds;
				batch.Basis = Basis.Identity;
				batch.From = exact.Count < origins.Count ? origins[exact.Count]
					: _dealerPoint + DealerOffset(exact.Count);
				batch.To = batch.From;
				batch.Phase = ChipPhase.InPot;
				batch.Pile.Visible = false;
				batch.Pile.Transform = new Transform3D(Basis.Identity, batch.From);
				batch.Pile.Spread = 0.0f;
				batch.Pile.FlightProgress = 1.0f;
				batch.Pile.SetRuns(new[] { run });
				batch.Pile.Visible = true;
				exact.Add(batch);
				recipients.Add(winner.PlayerId);
			}
			remainingBudget = Mathf.Max(0, remainingBudget - grouped.Count);
		}
		DealerChangeInProgress = false;
		DealerChangeCompleted = true;
		EmitSignal(SignalName.DealerChangeFinished);
		StartPayout(exact, recipients);
	}

	private void StartPayout(IReadOnlyList<ChipBatch> batches, IReadOnlyList<string> recipients)
	{
		var arrivals = new Dictionary<(string Player, int Denomination), int>();
		var order = 0;
		for (var i = 0; i < batches.Count && i < recipients.Count; i++)
		{
			var batch = batches[i];
			var winner = recipients[i];
			if (_seatPlaces == null || !_seatPlaces(winner, out var basis, out var stack))
				continue;
			var denomination = PokerChipAnimator.DenominationOf(batch);
			var key = (winner, denomination);
			var arrival = arrivals.GetValueOrDefault(key);
			arrivals[key] = arrival + batch.Pile.ChipCount;
			batch.WinnerId = winner;
			batch.From = batch.Pile.Position;
			batch.To = stack + basis * (_winnerOffset?.Invoke(
				winner, denomination, arrival, batch.Pile) ?? Vector3.Zero);
			batch.FromBasis = batch.Pile.Basis;
			batch.ToBasis = basis;
			batch.Progress = 0.0f;
			batch.Delay = order++ * _profile.ChipPayoutStagger;
			batch.JustStarted = true;
			batch.Phase = ChipPhase.ToWinner;
			batch.Pile.FlightProgress = 0.0f;
			batch.Pile.Spread = 0.0f;
		}
	}

	private static Vector3 DealerOffset(int index)
	{
		const float spacing = 0.018f;
		return new Vector3((index % 5 - 2) * spacing, index / 5 * 0.0037f, 0.0f);
	}
}
