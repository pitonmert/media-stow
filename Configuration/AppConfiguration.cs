namespace MediaStow.Configuration;

public class AppConfiguration
{
    public bool Verbose { get; set; }
    public bool Quiet { get; set; }

    public void ParseGlobalOptions(ref string[] args)
    {
        var remaining = new List<string>();
        foreach (var arg in args)
        {
            switch (arg)
            {
                case "-v" or "--verbose":
                    Verbose = true;
                    break;
                case "-q" or "--quiet":
                    Quiet = true;
                    break;
                default:
                    remaining.Add(arg);
                    break;
            }
        }
        args = remaining.ToArray();
    }
}
