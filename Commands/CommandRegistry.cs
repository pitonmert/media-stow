using MediaStow.Abstractions;

namespace MediaStow.Commands;

public class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ICommand command)
    {
        _commands[command.Name] = command;
    }

    public ICommand? GetCommand(string name)
    {
        return _commands.TryGetValue(name, out var command) ? command : null;
    }

    public IEnumerable<ICommand> GetAllCommands()
    {
        return _commands.Values.Distinct();
    }
}
