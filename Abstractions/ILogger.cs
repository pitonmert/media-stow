namespace MediaStow.Abstractions;

public interface ILogger
{
    void Info(string message);
    void Verbose(string message);
    void Warning(string message);
    void Error(string message);
    void Header(string title);
    void ShowProgress(int current, int total, string prefix = "Processing");
    void ClearProgress();
}
