namespace MediaStow.Abstractions;

public interface IUserInteraction
{
    bool Confirm(string message);
}
