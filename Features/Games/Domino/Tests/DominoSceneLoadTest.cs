using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Domino.Rules;
using Godot;

/// <summary>
/// Checks the wiring the rules tests cannot see: that the scenes parse and point at each other,
/// that every RPC carries the mode the protocol assumes, that no broadcast can carry tile
/// contents, and that the hand view is still a swappable seam.
/// </summary>
public partial class DominoSceneLoadTest : Node
{
	private int _passed;
	private int _failed;

	public override void _Ready()
	{
		GD.Print("=== Teste de integração do dominó ===");

		TestScenesLoad();
		TestGameScene();
		TestRpcModes();
		TestHandsNeverBroadcast();
		TestHandViewIsASeam();
		TestControllerIsAGameController();
		TestHandStateMachine();
		TestSeatPresenter();
		TestEveryTableGetsTheCamera();
		TestCameraRigs();
		TestArtPack();
		TestPlayerCountGate();

		GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
		if (_failed > 0)
			GD.PushWarning($"{_failed} verificação(ões) de integração do dominó falharam.");

		GetTree().Quit(_failed > 0 ? 1 : 0);
	}

	private void TestScenesLoad()
	{
		LoadScene("res://Features/Games/Domino/Domino.tscn");
		LoadScene("res://Features/Games/Domino/GameController/DominoController.tscn");
		LoadScene("res://Features/Games/Domino/GameController/Views/DominoHand3DView.tscn");
		LoadScene("res://Features/Games/Domino/Components/Tiles/DominoTile.tscn");
	}

	private PackedScene LoadScene(string path)
	{
		var scene = GD.Load<PackedScene>(path);
		var ok = scene != null && scene.CanInstantiate();
		Check($"carrega {path.GetFile()}", ok);
		return ok ? scene : null;
	}

	private void TestGameScene()
	{
		var scene = GD.Load<PackedScene>("res://Features/Games/Domino/Domino.tscn");
		if (scene == null)
			return;

		var game = scene.Instantiate<DominoGame>();
		AddChild(game);

		Check("jogo aponta para o controlador e para o apresentador da corrente",
			game.GameControllerScene != null && game.ChainPresenter != null);

		Check("jogo tem um resolvedor de dominó ligado pelo GameModeHandler",
			game.Resolver != null);

		// Four seats, each with the eye marker the seat camera is built around. Without it the
		// player sits down with their head on the floor.
		var seats = 0;
		var allHaveEyes = true;
		for (var i = 0; i < game.Seats.GetChildCount(); i++)
		{
			if (game.Seats.GetChild(i) is not Marker3D seat)
				continue;

			seats++;
			if (seat.GetNodeOrNull<Node3D>("SeatView") == null)
				allHaveEyes = false;
		}

		Check($"a mesa tem quatro assentos (achou {seats})", seats == 4);
		Check("todo assento tem o ponto de vista do jogador", allHaveEyes);

		// The seat's yaw is what the player's body and the seat camera are aligned to, so a chair
		// facing the wrong way seats someone with their back to the game. This is checked rather
		// than eyeballed because opening the scene in the editor has already silently flattened
		// two of these rotations once.
		var worstOff = 0.0f;
		for (var i = 0; i < game.Seats.GetChildCount(); i++)
		{
			if (game.Seats.GetChild(i) is not Marker3D seat)
				continue;

			// A Node3D looks down its own -Z.
			var facing = -seat.GlobalTransform.Basis.Z;
			var towardTable = (game.GlobalPosition - seat.GlobalPosition) with { Y = 0.0f };
			if (towardTable.LengthSquared() < 1e-6f)
				continue;

			var offBy = Mathf.RadToDeg(
				new Vector2(facing.X, facing.Z).AngleTo(new Vector2(towardTable.X, towardTable.Z)));
			worstOff = Mathf.Max(worstOff, Mathf.Abs(offBy));
		}

		Check($"todo assento está virado para a mesa (pior desvio {worstOff:F1}°)", worstOff < 1.0f);

		// The tiles the presenter lays down have to be the size the layout reserved for them,
		// otherwise the table looks overlapped while the layout test still passes.
		var spec = game.ChainPresenter.Spec;
		Check($"o apresentador usa a mesma medida do layout ({spec.TileLength:F3} x {spec.TileWidth:F3} m)",
			Mathf.IsEqualApprox(spec.TileLength, LayoutSpec.Default.TileLength)
			&& Mathf.IsEqualApprox(spec.TileWidth, LayoutSpec.Default.TileWidth));

		var full = DominoChainLayout.Rebuild(FullChainPlays(), spec);
		Check("a corrente completa cabe na mesa configurada na cena", !full.OverflowedTable);

		game.QueueFree();
	}

	private void TestRpcModes()
	{
		// A request must be AnyPeer or a client could never send it; a server message must be
		// Authority or a client would refuse it. Everything is reliable: a dropped play would
		// stall the match.
		CheckRpc<DominoTurnResolver>("PlayTileOnServer", MultiplayerApi.RpcMode.AnyPeer, false);
		CheckRpc<DominoTurnResolver>("DrawTileOnServer", MultiplayerApi.RpcMode.AnyPeer, false);
		CheckRpc<DominoTurnResolver>("PassOnServer", MultiplayerApi.RpcMode.AnyPeer, false);
		CheckRpc<DominoTurnResolver>("ReceiveHand", MultiplayerApi.RpcMode.Authority, false);
		CheckRpc<DominoTurnResolver>("ReceiveActionRejected", MultiplayerApi.RpcMode.Authority, false);
		CheckRpc<DominoTurnResolver>("ReceiveFullState", MultiplayerApi.RpcMode.Authority, false);
	}

	private void CheckRpc<T>(string methodName, MultiplayerApi.RpcMode mode, bool callLocal)
	{
		var method = typeof(T).GetMethod(methodName,
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		var attribute = method?.GetCustomAttribute<RpcAttribute>();

		Check($"{methodName} é [Rpc({mode}, CallLocal={callLocal}, Reliable)]",
			attribute != null
			&& attribute.Mode == mode
			&& attribute.CallLocal == callLocal
			&& attribute.TransferMode == MultiplayerPeer.TransferModeEnum.Reliable);
	}

	/// <summary>
	/// The secrecy rule, enforced statically. Tile contents are int arrays; if one ever appears on
	/// a method that is not the single targeted hand channel, some refactor has started leaking
	/// hands to the whole table.
	/// </summary>
	private void TestHandsNeverBroadcast()
	{
		var carriers = typeof(DominoTurnResolver)
			.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			.Where(method => method.GetCustomAttribute<RpcAttribute>() != null)
			.Where(method => method.GetParameters()
				.Any(parameter => parameter.ParameterType == typeof(int[])
								  || parameter.ParameterType == typeof(long[])))
			.Select(method => method.Name)
			.ToList();

		Check($"só ReceiveHand transporta peças ({string.Join(", ", carriers)})",
			carriers.Count == 1 && carriers[0] == "ReceiveHand");

		// And the seed is even worse than a hand: it reconstructs all of them at once.
		var leaksSeed = typeof(DominoTurnResolver)
			.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			.Where(method => method.GetCustomAttribute<RpcAttribute>() != null)
			.Any(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ulong)));

		Check("nenhuma RPC transporta a semente da distribuição", !leaksSeed);

		// The public state DominoGame holds must have nowhere to put another player's tiles. Two
		// int arrays are allowed, each for a stated reason, and anything else appearing here has to
		// be justified rather than slipped in:
		//   LocalHand     — this peer's OWN tiles, which arrived through the targeted ReceiveHand.
		//   BoneyardSlots — places on the table, never tile ids. DominoMatchTest pins that they
		//                   are published as 0..N-1, which is what keeps the stock face down.
		var allowed = new HashSet<string> { nameof(DominoGame.LocalHand), nameof(DominoGame.BoneyardSlots) };

		var publicIntArrays = typeof(DominoGame)
			.GetFields(BindingFlags.Instance | BindingFlags.Public)
			.Where(field => field.FieldType == typeof(int[]))
			.Select(field => field.Name)
			.ToList();

		var unexpected = publicIntArrays.Where(name => !allowed.Contains(name)).ToList();

		Check($"o estado público não ganhou nenhum vetor de peças novo "
			  + $"([{string.Join(", ", publicIntArrays)}]"
			  + (unexpected.Count > 0 ? $", inesperado: {string.Join(", ", unexpected)}" : "") + ")",
			unexpected.Count == 0 && publicIntArrays.Contains(nameof(DominoGame.LocalHand)));
	}

	/// <summary>
	/// What kept the swap from the panel of buttons to the 3D hand a one-line change, and what will
	/// keep the animated rig a drop-in after it: the controller depends on the abstract view, the
	/// shipping view is a subclass of it, and the intents are signals on the base.
	/// </summary>
	private void TestHandViewIsASeam()
	{
		Check("a mão em 3D é uma DominoHandView",
			typeof(DominoHandView).IsAssignableFrom(typeof(DominoHand3DView)));

		var overrides = new[]
		{
			"Refresh", "SetInteractive", "SetTopViewActive", "ShowNotice", "ShowRejection", "Clear",
		};
		var allOverridden = overrides.All(name =>
			typeof(DominoHand3DView).GetMethod(name,
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				?.DeclaringType == typeof(DominoHand3DView));

		Check("a mão em 3D implementa toda a superfície da abstração", allOverridden);

		// The controller is wired to the 3D hand and to nothing more specific than the abstraction.
		var controllerScene = GD.Load<PackedScene>(
			"res://Features/Games/Domino/GameController/DominoController.tscn");
		var wired = controllerScene?.Instantiate<DominoController>();
		Check("o controlador aponta para a mão em 3D",
			wired?.HandViewScene?.Instantiate() is DominoHand3DView);
		wired?.QueueFree();

		var signals = new[] { "TilePlayRequested", "DrawRequested", "PassRequested", "SurrenderRequested" };
		var allDeclared = signals.All(name =>
			typeof(DominoHandView).GetNestedType("SignalName", BindingFlags.Public)
				?.GetField(name, BindingFlags.Public | BindingFlags.Static) != null);

		Check("a abstração declara os quatro sinais de intenção", allDeclared);

		// The controller must not know the CONCRETE view either. This caught a real slip: reaching
		// for the notice label while wiring "hold to leave the table" quietly re-typed the field to
		// DominoHand3DView, which would have made the animated rig a rewrite instead of a swap.
		var controllerFields = typeof(DominoController)
			.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			.ToList();

		var concreteViewFields = controllerFields
			.Where(field => typeof(DominoHandView).IsAssignableFrom(field.FieldType)
							&& field.FieldType != typeof(DominoHandView))
			.Select(field => field.Name)
			.ToList();

		Check($"o controlador fala só com a abstração da mão "
			  + (concreteViewFields.Count > 0 ? $"(concreto: {string.Join(", ", concreteViewFields)})" : ""),
			concreteViewFields.Count == 0);

		// And it must not know what a Control is, or the view stops being a drop-in at all.
		var controllerTouchesUi = controllerFields
			.Any(field => typeof(Control).IsAssignableFrom(field.FieldType)
						  || typeof(CanvasLayer).IsAssignableFrom(field.FieldType));

		Check("o controlador não referencia nenhum nó de interface", !controllerTouchesUi);
	}

	/// <summary>
	/// The hand's states are registered by the Type each one sets in its constructor, not by node
	/// name, so a typo there registers a state nobody can ever reach — and the machine would sit in
	/// whatever it started in with no error.
	/// </summary>
	private void TestHandStateMachine()
	{
		var scene = GD.Load<PackedScene>(
			"res://Features/Games/Domino/GameController/Views/DominoHand3DView.tscn");
		if (scene == null)
			return;

		var view = scene.Instantiate<DominoHand3DView>();
		AddChild(view);

		var machine = view.GetNodeOrNull<StateMachine>("StateMachine");
		Check("a mão tem máquina de estados", machine != null);

		if (machine == null)
		{
			view.QueueFree();
			return;
		}

		var expected = new[]
		{
			StatesRef.DominoHandIdle, StatesRef.DominoHandLooking, StatesRef.DominoHandAiming,
			StatesRef.DominoHandDrawing, StatesRef.DominoHandPlacing,
		};

		var missing = expected.Where(type => !machine.States.ContainsKey(type)).ToList();
		Check($"os cinco estados da mão estão registrados "
			  + (missing.Count > 0 ? $"(faltando: {string.Join(", ", missing)})" : "(todos)"),
			missing.Count == 0);

		Check($"a mão começa escondida, em {StatesRef.DominoHandIdle}",
			machine.Current != null && machine.Current.Type == StatesRef.DominoHandIdle);

		// Local presentation, like the cue: the play itself travels through the resolver, so
		// replicating these states would just be chatter.
		Check("os estados da mão não são replicados", machine.AuthorityStateSynchronizer == null);
		Check("a mão só lê input de quem é dono dela",
			machine.CheckForMultiplayerAuthorityOnStateHandleInput);

		view.QueueFree();

		Check("a ação de cancelar está mapeada", InputMap.HasAction("cancel_action"));
		Check("a ação de sair da mesa está mapeada", InputMap.HasAction("leave_table"));

		// Free look is no longer an action: with the mouse captured, looking around is simply
		// moving it, and the right button was freed up to cancel.
		Check("free_look foi removida", !InputMap.HasAction("free_look"));
	}

	/// <summary>
	/// The table's furniture replaces the panel: it must be driven entirely off public state, and
	/// the stock the crosshair aims at must be laid out with the same numbers it is drawn with.
	/// </summary>
	private void TestSeatPresenter()
	{
		var scene = GD.Load<PackedScene>("res://Features/Games/Domino/Domino.tscn");
		if (scene == null)
			return;

		var game = scene.Instantiate<DominoGame>();
		AddChild(game);

		var presenter = game.GetNodeOrNull<DominoSeatPresenter>("SeatPresenter");
		Check("a mesa tem o apresentador de assentos", presenter != null);

		if (presenter == null)
		{
			game.QueueFree();
			return;
		}

		Check("o apresentador conhece a mesa e os assentos",
			presenter.ChainPresenter != null && presenter.Seats != null && presenter.TileScene != null);

		// One source for where the stock lies, or the crosshair aims at empty cloth.
		var drawn = presenter.ChainPresenter.Spec;
		Check($"o monte é medido pela mesma peça da corrente "
			  + $"({game.StockSpec.TileLength:F3} x {game.StockSpec.TileWidth:F3} m)",
			Mathf.IsEqualApprox(game.StockSpec.TileLength, drawn.TileLength)
			&& Mathf.IsEqualApprox(game.StockSpec.TileWidth, drawn.TileWidth));

		var bounds = DominoBoneyardLayout.Bounds(21, game.StockSpec);
		Check($"o monte configurado na cena não invade a área de jogo "
			  + $"(começa em z={bounds.Position.Y:F3}, área vai até {drawn.PlayHalfExtents.Y:F3})",
			bounds.Position.Y > drawn.PlayHalfExtents.Y);

		game.QueueFree();
	}

	private void TestControllerIsAGameController()
	{
		Check("o controlador de dominó é um GameController",
			typeof(GameController).IsAssignableFrom(typeof(DominoController)));

		var controller = new DominoController();
		Check("o dominó desliga a troca de controle (E) durante a partida",
			!controller.AllowsControlSwitch);
		controller.Free();
	}

	/// <summary>
	/// Loads the real Main scene and checks every table in it came away with a camera.
	///
	/// Main used to wire one table by path, so adding the domino table left its game with a null
	/// camera — and because every view change is null-guarded, the symptom was a player who sat
	/// down and then could neither look around nor switch to the overhead view. Nothing threw.
	/// </summary>
	private void TestEveryTableGetsTheCamera()
	{
		var scene = GD.Load<PackedScene>("res://Main.tscn");
		if (scene == null)
		{
			Check("carrega Main.tscn", false);
			return;
		}

		var main = scene.Instantiate();
		AddChild(main);

		var tables = new List<Table>();
		CollectTables(main, tables);

		Check($"o nível tem mais de uma mesa (achou {tables.Count})", tables.Count >= 2);

		var wired = 0;
		var missing = new List<string>();
		foreach (var table in tables)
		{
			var game = table.CurrentTableGame;
			if (game == null)
			{
				missing.Add($"{table.Name} (sem jogo)");
				continue;
			}

			// Read through the concrete games, because TableGame keeps the camera in whatever
			// field the mode wants rather than on the base.
			var hasCamera = game switch
			{
				DominoGame domino => domino.Camera != null,
				PoolGame pool => pool.Camera != null,
				_ => true,
			};

			if (hasCamera)
				wired++;
			else
				missing.Add((string)table.Name);
		}

		Check($"toda mesa do nível recebeu a câmera ({wired}/{tables.Count}"
			  + (missing.Count > 0 ? $", faltou: {string.Join(", ", missing)}" : "") + ")",
			missing.Count == 0 && tables.Count > 0);

		main.QueueFree();
	}

	private static void CollectTables(Node node, List<Table> into)
	{
		if (node is Table table)
			into.Add(table);

		foreach (var child in node.GetChildren())
			CollectTables(child, into);
	}

	/// <summary>
	/// Both camera rigs live on the controller rather than in the shared table scene, which is
	/// what lets each player look around on their own and see the overhead view from their own
	/// side. If a rig went missing the player would simply keep the walking camera.
	/// </summary>
	private void TestCameraRigs()
	{
		var scene = GD.Load<PackedScene>("res://Features/Games/Domino/GameController/DominoController.tscn");
		if (scene == null)
			return;

		var controller = scene.Instantiate<DominoController>();
		AddChild(controller);

		Check("o controlador tem o rig de olhar em volta",
			controller.GetNodeOrNull<RemoteTransform3D>("LookRig/LookPitch/RemoteSeat") != null);
		Check("o controlador tem o rig da vista de cima",
			controller.GetNodeOrNull<RemoteTransform3D>("TopRig/RemoteTop") != null);

		// Yaw is clamped rather than free so a seated player never ends up facing backwards.
		Check($"o giro do pescoço é limitado ({controller.MaxYawDeg}°)",
			controller.MaxYawDeg is > 0.0f and <= 180.0f);
		Check($"a inclinação é limitada ({controller.MinPitchDeg}° a {controller.MaxPitchDeg}°)",
			controller.MinPitchDeg < controller.MaxPitchDeg
			&& controller.MinPitchDeg >= -90.0f && controller.MaxPitchDeg <= 90.0f);
		Check($"o descanso do olhar aponta para a mesa ({controller.RestPitchDeg}°)",
			controller.RestPitchDeg < 0.0f
			&& controller.RestPitchDeg >= controller.MinPitchDeg
			&& controller.RestPitchDeg <= controller.MaxPitchDeg);

		// Both rigs are placed in world space from the seat, so inheriting the player's transform
		// would drag them around.
		Check("os rigs de câmera ignoram a transformação do jogador",
			controller.GetNode<Node3D>("LookRig").TopLevel
			&& controller.GetNode<Node3D>("TopRig").TopLevel);

		// With the mouse captured the overhead view has to be movable, or a crosshair locked to
		// the screen centre could only ever point at the middle of the table.
		Check($"a vista de cima pode ser deslocada "
			  + $"({controller.TopPanSensitivity:F4} por pixel, limite {controller.TopPanLimit})",
			controller.TopPanSensitivity > 0.0f
			&& controller.TopPanLimit.X > 0.0f && controller.TopPanLimit.Y > 0.0f);

		// Getting up mid-match forfeits, so it must be a hold rather than a tap.
		Check($"sair da mesa exige segurar ({controller.LeaveHoldSeconds:F1}s)",
			controller.LeaveHoldSeconds >= 0.5f);

		controller.QueueFree();

		Check("a ação da vista de cima está mapeada", InputMap.HasAction("toggle_top_view"));
	}

	/// <summary>
	/// The art pack ships all 28 faces in one .glb with nothing in the node names to say which is
	/// which, so the mapping is recorded by hand. This is what catches a mistyped or duplicated
	/// entry — which would otherwise show up as two tiles wearing the same face mid-match.
	/// </summary>
	private void TestArtPack()
	{
		Check("o pacote de arte do dominó foi carregado", DominoTileMeshes.IsAvailable);
		if (!DominoTileMeshes.IsAvailable)
			return;

		var missing = 0;
		var shared = 0;
		var seen = new Dictionary<ulong, int>();

		for (var tileId = 0; tileId < DominoTileId.Count; tileId++)
		{
			var mesh = DominoTileMeshes.For(tileId);
			if (mesh == null)
			{
				missing++;
				continue;
			}

			// A duplicated entry in the table would hand two tiles the same face.
			if (!seen.TryAdd(mesh.GetInstanceId(), tileId))
				shared++;
		}

		Check($"as 28 peças têm malha própria (faltando {missing}, repetidas {shared})",
			missing == 0 && shared == 0);

		// The mesh has to sit centred on the tile node and lying flat, or the chain the layout
		// computed would float, sink or stand on edge.
		var sample = DominoTileMeshes.For(DominoTileId.From(3, 5));
		var transform = DominoTileMeshes.TransformFor(DominoTileId.From(3, 5));
		var centre = transform * sample.GetAabb().GetCenter();
		Check($"a malha fica centrada no nó da peça (desvio {centre.Length() * 1000.0f:F2} mm)",
			centre.Length() < 1e-4f);

		var size = (transform.Basis * sample.GetAabb().Size).Abs();
		var spec = LayoutSpec.Default;
		Check($"a peça sai do tamanho que o layout reservou ({size.X * 1000.0f:F1} x "
			  + $"{size.Z * 1000.0f:F1} x {size.Y * 1000.0f:F1} mm)",
			Mathf.IsEqualApprox(size.X, spec.TileWidth, 1e-4f)
			&& Mathf.IsEqualApprox(size.Z, spec.TileLength, 1e-4f)
			&& Mathf.IsEqualApprox(size.Y, spec.TileThickness, 1e-4f));
	}

	private void TestPlayerCountGate()
	{
		var game = new DominoGame { AllowSoloDebug = false };

		Check("dominó recusa partida com um jogador só", !game.CanStartWith(1));
		Check("dominó aceita de dois a quatro jogadores",
			game.CanStartWith(2) && game.CanStartWith(3) && game.CanStartWith(4));
		Check("dominó recusa um quinto jogador", !game.CanStartWith(5));
		Check("partida entre dois jogadores conta para o ranking", game.CountsWinsForRanking);

		game.AllowSoloDebug = true;
		Check("o modo de teste solo libera a partida de um jogador", game.CanStartWith(1));

		game.Free();
	}

	/// <summary>A legal 28-tile chain, built the same way the layout test builds one.</summary>
	private static List<PlayRecord> FullChainPlays()
	{
		var used = new bool[DominoTileId.Count];
		var stack = new Stack<int>();
		var walk = new List<int>();
		stack.Push(0);

		while (stack.Count > 0)
		{
			var vertex = stack.Peek();
			var advanced = false;

			for (var other = 0; other <= DominoTileId.MaxPips; other++)
			{
				var tileId = DominoTileId.From(vertex, other);
				if (used[tileId])
					continue;

				used[tileId] = true;
				stack.Push(other);
				advanced = true;
				break;
			}

			if (!advanced)
				walk.Add(stack.Pop());
		}

		var plays = new List<PlayRecord>(DominoTileId.Count);
		var openingTile = DominoTileId.From(walk[0], walk[1]);
		plays.Add(new PlayRecord("1", openingTile, ChainEnd.Right));

		var frontEnd = DominoTileId.High(openingTile) == walk[1] ? ChainEnd.Right : ChainEnd.Left;
		var backEnd = frontEnd == ChainEnd.Right ? ChainEnd.Left : ChainEnd.Right;

		var lo = 1;
		var hi = DominoTileId.Count - 1;
		var takeFront = true;

		while (lo <= hi)
		{
			if (takeFront)
			{
				plays.Add(new PlayRecord("1", DominoTileId.From(walk[lo], walk[lo + 1]), frontEnd));
				lo++;
			}
			else
			{
				plays.Add(new PlayRecord("1", DominoTileId.From(walk[hi], walk[hi + 1]), backEnd));
				hi--;
			}

			takeFront = !takeFront;
		}

		return plays;
	}

	private void Check(string label, bool condition)
	{
		if (condition)
		{
			_passed++;
			GD.Print($"  OK   {label}");
		}
		else
		{
			_failed++;
			GD.Print($"  FALHA {label}");
		}
	}
}
