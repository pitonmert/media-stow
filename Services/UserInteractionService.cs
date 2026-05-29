using MediaStow.Abstractions;

namespace MediaStow.Services;

public class UserInteractionService : IUserInteraction
{
    public bool Confirm(string message)
    {
        Console.Write($"{message} (y/N): ");
        var response = Console.ReadLine()?.Trim().ToLower();
        return response == "y";
    }
}
