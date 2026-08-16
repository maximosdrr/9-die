using System.Linq;
using Godot;
using Godot.Collections;
using Poker.Rules;

/// <summary>
/// Renders the cloth to a PNG so the presentation can be LOOKED at rather than only asserted.
///
/// Temporary scaffolding, not part of the suite: it needs a real renderer, so it cannot run headless
/// alongside the other tests. It stages one moment — chips thrown into the middle and a hand in the
/// muck — from the same public state a real table would be holding.
/// </summary>
public partial class PokerVisualCheck : Node3D
{
    [Export] public string OutputPath = "user://poker_visual.png";

    /// <summary>Frames of settling before the shot, so the throws and the muck are where they end up.</summary>
    [Export] public int WarmUpFrames = 20;

    private PokerGame _game;
    private int _frames;
    private bool _shot;
    private bool _captureRevealHold;
    private bool _captureRevealMotion;
    private bool _capturePayoutLoose;

    public override void _Ready()
    {
        var players = BuildPlayers("1", "2");
        var table = BuildTable();

        _game = table.CurrentTableGame as PokerGame;
        if (_game == null)
        {
            GD.PushError("A cena de poker não instanciou.");
            GetTree().Quit(1);
            return;
        }

        var camera = new GlobalCamera();
        AddChild(camera);
        _game.SetCamera(camera);

        _game.AllowSoloDebug = true;
        _game.StartingStack = 500;
        _game.SmallBlind = 5;
        _game.BigBlind = 10;
        // This harness submits an entire hand before its first rendered frame, unlike real play. Speed
        // only the queued reveal so the final comparison is what the fixed-time screenshot captures.
        _game.BoardPresenter.FlipSeconds = 0.28f;
        _game.SeatPresenter.ShowdownCardSeconds = 0.48f;
        _captureRevealHold = OS.GetEnvironment("POKER_CAPTURE_REVEAL") == "1";
        _captureRevealMotion = OS.GetEnvironment("POKER_CAPTURE_REVEAL_MOTION") == "1";
        _capturePayoutLoose = OS.GetEnvironment("POKER_CAPTURE_PAYOUT_LOOSE") == "1";
        if (_captureRevealHold)
            _game.SeatPresenter.ShowdownRevealHoldSeconds = 3600.0f;
        if (_capturePayoutLoose)
        {
            _game.SeatPresenter.ShowdownRevealHoldSeconds = 0.01f;
            _game.SeatPresenter.PresentationProfile.RankedHandsReadingSeconds = 0.01f;
            _game.SeatPresenter.PresentationProfile.WinnerLooseHoldSeconds = 3600.0f;
        }

        var order = new Array { "1", "2" };
        _game.TurnOrder = order;
        _game.TurnOwner = players["1"];
        _game.Player = players["1"];

        var resolver = _game.Resolver;
        resolver.AutoAdvanceHands = false;
        resolver.FoldedHandSeconds = 0.0f;
        resolver.WaitAnimationsEndToNextTurn = false;

        _game.SetupMatch(order, "1");

        // Match the blind and let the big blind check its option, then check through the board. Keeping
        // this harness heads-up makes the final two comparison rows easy to inspect in one screenshot.
        Act(PokerActionKind.Call, 10);
        Act(PokerActionKind.Check, 0);

        // Check the remaining players through the board so the screenshot covers the ranked best-five
        // comparison rather than only the betting layout.
        var safety = 0;
        while (!_game.HandSettled && safety++ < 20)
        {
            if (_game.ShowdownWaiting)
            {
                foreach (var playerId in _game.PendingShowdownReveals.ToArray())
                    resolver.ApplyShowdownRevealFor(playerId);
                continue;
            }

            var actor = _game.TurnOwnerId;
            var state = _game.BetStateOf(actor);
            var options = PokerBetting.LegalActions(
                state,
                _game.CurrentBet,
                _game.MinRaiseIncrement,
                _game.HasOpponentWhoCanAct(actor));
            var option = options.FirstOrDefault(candidate => candidate.Kind == PokerActionKind.Check);
            if (option.Kind == PokerActionKind.None)
                option = options.First(candidate => candidate.Kind == PokerActionKind.Call);

            _game.Resolver.ApplyActionFor(actor, _game.TurnToken, (int)option.Kind, option.MinTotal);
        }

        // Advance the local presentation deterministically. The harness submits all actions in one
        // method call, whereas a real table naturally gets many rendered frames between them.
        var revealMotionFrames = 0;
        for (var frame = 0; frame < 2400; frame++)
        {
            _game.SeatPresenter._Process(1.0 / 60.0);
            _game.BoardPresenter._Process(1.0 / 60.0);

            if (_captureRevealMotion)
            {
                if (_game.SeatPresenter.RemoteRevealedHandsInMotion > 0)
                    revealMotionFrames++;
                if (revealMotionFrames >= 12)
                    break;
                continue;
            }
            if (_capturePayoutLoose)
            {
                if (_game.SeatPresenter.PayoutHasLooseDelivery
                    && !_game.SeatPresenter.WinnerOrganizationInProgress)
                    break;
                continue;
            }
            if (_captureRevealHold)
            {
                if (_game.SeatPresenter.ShowdownRevealHoldElapsed > 0.0f)
                    break;
                continue;
            }
            if (_game.SeatPresenter.ShowdownPresentationSettled)
                break;
        }

        BuildCamera();
    }

    private void Act(PokerActionKind kind, int total)
    {
        var actor = _game.TurnOwnerId;
        if (string.IsNullOrEmpty(actor))
            return;

        var state = _game.BetStateOf(actor);
        var options = PokerBetting.LegalActions(
            state,
            _game.CurrentBet,
            _game.MinRaiseIncrement,
            _game.HasOpponentWhoCanAct(actor));
        var option = options.FirstOrDefault(o => o.Kind == kind);

        if (option.Kind == PokerActionKind.None)
            option = options.First();

        var amount = Mathf.Clamp(total, option.MinTotal, option.MaxTotal);
        _game.Resolver.ApplyActionFor(actor, _game.TurnToken, (int)option.Kind, amount);
    }

    /// <summary>Uses the real seat pose and FOV, so this diagnostic matches normal play.</summary>
    private void BuildCamera()
    {
        var controllerScene = GD.Load<PackedScene>(
            "res://Games/Poker/GameController/PokerController.tscn");
        var controller = controllerScene?.Instantiate<PokerController>();
        var seat = _game.SeatFor("1");
        var eye = seat?.GetNodeOrNull<Node3D>("SeatView") ?? seat;
        var eyeTransform = eye?.GlobalTransform ?? Transform3D.Identity;
        var eyeBasis = eyeTransform.Basis.Orthonormalized();
        var offset = controller?.SeatViewOffset ?? Vector3.Zero;
        var pitch = controller?.RestPitchDeg ?? -35.0f;

        var camera = new Camera3D
        {
            Fov = controller?.SeatFov ?? 48.0f,
            Current = true,
            GlobalTransform = new Transform3D(
                eyeBasis * new Basis(Vector3.Right, Mathf.DegToRad(pitch)),
                eyeTransform.Origin + eyeBasis * offset),
        };
        AddChild(camera);
        controller?.Free();

        var light = new DirectionalLight3D { LightEnergy = 1.4f, ShadowEnabled = true };
        AddChild(light);
        light.GlobalPosition = new Vector3(0.6f, 2.4f, 0.9f);
        light.LookAt(new Vector3(0.0f, 0.718f, 0.0f), Vector3.Up);

        var fill = new OmniLight3D { LightEnergy = 1.1f, OmniRange = 6.0f };
        AddChild(fill);
        fill.GlobalPosition = new Vector3(-0.8f, 1.7f, 0.6f);

        var ambient = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.06f, 0.07f, 0.09f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.5f, 0.52f, 0.58f),
                AmbientLightEnergy = 0.9f,
            },
        };

        AddChild(ambient);
    }

    public override void _Process(double delta)
    {
        if (_shot)
            return;

        if (++_frames < WarmUpFrames)
            return;

        _shot = true;
        GD.Print($"SHOWDOWN: settled={_game.SeatPresenter?.ShowdownPresentationSettled} "
            + $"cards={_game.SeatPresenter?.ShowdownDisplayedCardCount} "
            + $"order={string.Join(",", _game.SeatPresenter?.ShowdownDisplayOrder ?? System.Array.Empty<string>())} "
            + _game.SeatPresenter?.DebugShowdownState());
        GD.Print("BOARD: " + _game.BoardPresenter?.DebugState());

        var image = GetViewport().GetTexture().GetImage();
        image.SavePng(OutputPath);
        GD.Print($"IMAGEM: {ProjectSettings.GlobalizePath(OutputPath)}");

        GetTree().Quit(0);
    }

    private System.Collections.Generic.Dictionary<string, Player> BuildPlayers(params string[] ids)
    {
        var container = new Node3D { Name = "PlayersContainer" };
        AddChild(container);
        PlayerRegistry.Instance.PlayersContainer = container;

        var scene = GD.Load<PackedScene>("res://World/Player/Player.tscn");
        var players = new System.Collections.Generic.Dictionary<string, Player>();

        foreach (var id in ids)
        {
            var player = scene.Instantiate<Player>();
            player.Name = id;
            player.Id = int.Parse(id);
            container.AddChild(player);
            // Opt-in body capture validates the real chair/character proportions without changing
            // the established cloth-only regression image.
            player.Visible = OS.GetEnvironment("POKER_CAPTURE_PLAYERS") == "1";
            players[id] = player;
        }

        return players;
    }

    private Table BuildTable()
    {
        var table = GD.Load<PackedScene>("res://Shared/Table/Table.tscn").Instantiate<Table>();

        table.EnableNetworkTurnSynchronization = false;
        table.TableGameScene = GD.Load<PackedScene>("res://Games/Poker/Poker.tscn");

        AddChild(table);
        return table;
    }
}
