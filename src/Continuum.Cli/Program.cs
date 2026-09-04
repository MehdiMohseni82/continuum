using Continuum.Cli;

// The `continuum` command. Verb dispatch by hand: the whole surface is six verbs, and a command-line
// parsing package would be the only NuGet dependency in the chain from here down to Continuum.Client.

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var verb = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
var rest = args.Skip(1).ToArray();

try
{
    return verb switch
    {
        // Runs as a Stop hook, so stdout is a machine protocol — see RelayCommand.
        "relay-turn" => await RelayCommand.RunAsync(cts.Token),
        "join" => await RoomCommands.JoinAsync(rest, cts.Token),
        "leave" => RoomCommands.Leave(),
        "rooms" => await RoomsListCommand.RunAsync(cts.Token),
        "room" => await RoomStatusCommand.RunAsync(rest, cts.Token),
        "project" => await ProjectCommand.RunAsync(rest, cts.Token),
        "setup-relay" => SetupRelayCommand.Run(rest),
        "doctor" => await DoctorCommand.RunAsync(cts.Token),
        "help" or "--help" or "-h" => Help(0),
        "--version" or "version" => Version(),
        _ => Help(1, $"Unknown command '{verb}'."),
    };
}
catch (OperationCanceledException)
{
    return 130; // Ctrl-C
}

static int Version()
{
    var v = typeof(Config).Assembly.GetName().Version;
    Console.WriteLine($"continuum {v?.ToString(3) ?? "dev"}");
    return 0;
}

static int Help(int exitCode, string? error = null)
{
    var w = exitCode == 0 ? Console.Out : Console.Error;
    if (error is not null) { w.WriteLine(error); w.WriteLine(); }

    w.WriteLine("""
        continuum — join Continuum rooms from an interactive coding session.

          continuum doctor                     check what is and isn't wired up on this machine
          continuum rooms                      list rooms you can see, with their ids
          continuum room <room-id> [--follow]  who is driving it, who is quiet, and why
          continuum project [set <key>]        show, or declare, the workspace this repo belongs to
          continuum setup-relay [dir]          register the relay Stop hook for one folder
          continuum join <room-id> <agent>     join a room in this session (use /continuum-joinroom)
          continuum leave                      stop relaying this session
          continuum relay-turn                 the Stop hook itself; not for typing

        Configuration comes from CONTINUUM_BACKEND / CONTINUUM_TOKEN, or ~/.continuum/config.json.
        """);
    return exitCode;
}
