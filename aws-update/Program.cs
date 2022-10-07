using CommandLine.Options;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace aws_update
{
    internal class Program
    {
        static AWSCreds creds = null;
        static string profile = null;
        static string fileTargetPath = null;
        static int Main(string[] args)
        {

            bool show_help = false;

            var p = new OptionSet() {
                { "f|file:",
                    "the FILE to attempt to write creds into (or stdout, if not defined)", v => fileTargetPath = v},
                { "p|profile:",
                    "the PROFILE to get creds for", v => profile = v },
                { "h|help",  "show this message and exit",
                   v => show_help = v != null },
            };

            List<string> extra;
            try
            {
                extra = p.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Write("aws_update: ");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `aws_update --help' for more information.");
                return 1;
            }

            if (profile == null)
            {
                var profileGuess = System.Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                profileGuess = Path.Combine(profileGuess, ".aws", "config");
                try
                {
                    var lines = File.ReadAllLines(profileGuess);
                    var profileLine = lines.First(l => l.ToLower().StartsWith("[profile "));
                    profile = profileLine.Substring("[profile ".Length).TrimEnd(']');
                }
                catch { }
            }

            if (fileTargetPath != null && !File.Exists(fileTargetPath))
            {
                Console.Error.WriteLine($"output file {fileTargetPath} not found");
                return 2;
            }

            if (show_help)
            {
                ShowHelp(p);
                return 0;
            }

            creds = Attempt();
            if (creds == null)
            {
                Console.Error.WriteLine("couldn't deserialize, I bet the (real) error is \"must sign in\". so let's try that.");
                RunProc("aws", $"sso login --profile {profile}");
                creds = Attempt();

                if (creds == null)
                {
                    Console.Error.WriteLine("couldn't serialize, again. ┐(ﾟ ～ﾟ )┌");
                    return 3;
                }
            }

            Console.WriteLine("Hello, amazon!");

            if (fileTargetPath != null)
            {
                var fileContents = File.ReadAllText(fileTargetPath);
                fileContents = Regex.Replace(fileContents, "\"aws_session_token\" ?: ?\"[^\\\"]*\"", $"\"AWS_SESSION_TOKEN\" : \"{creds.SessionToken}\"", RegexOptions.IgnoreCase);
                File.WriteAllText(fileTargetPath, fileContents);
            }
            else
            {
                Console.WriteLine(creds);
            }
            return 0;
        }


        static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("Usage: aws_update [OPTIONS]+ message");
            Console.WriteLine("slightly reduce some pain of developing for AWS");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }
        static AWSCreds Attempt()
        {
            var procResult = RunProc("aws-sso-creds", $"helper --profile {profile}");
            var stdOut = procResult.Item1;
            var stdErr = procResult.Item2;
            Console.WriteLine(stdOut);
            Console.WriteLine(stdErr);
            return JsonConvert.DeserializeObject<AWSCreds>(stdOut);
        }
        static Tuple<string, string> RunProc(string program, string args)
        {
            var proc = new Process();
            proc.StartInfo = new ProcessStartInfo(program, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var stdOut = "";
            var stdErr = "";
            proc.Start();
            while (!proc.HasExited)
            {
                stdOut += proc.StandardOutput.ReadToEnd();
                stdErr += proc.StandardError.ReadToEnd();
            }
            return new Tuple<string, string>(stdOut, stdErr);
        }
    }

    internal class AWSCreds
    {
        public int page { get; set; }
        public string AccessKeyId { get; set; }
        public string SecretAccessKey { get; set; }
        public string SessionToken { get; set; }
        public DateTime Expiration { get; set; }
    }
}