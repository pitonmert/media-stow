namespace MediaStow.Abstractions;

public interface ICommand
{
    string Name { get; }
    string Description { get; }
    int Execute(string[] args);
}
