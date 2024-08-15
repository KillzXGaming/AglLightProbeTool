using BfresLibrary;
using AglLightProbeTool;

namespace AglLightProbeTool
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine($"Drag/drop a course_model.szs to use!");
                Console.WriteLine($"Outputs a usable course bglpbd file to fix character/obj lighting.");
                return;
            }

            foreach (var arg in args)
            {
                if (arg.EndsWith("bglpbd.szs") || arg.EndsWith("bglpbd"))
                {
                    //do nothing atm
                    Console.WriteLine($"Drag/drop a course_model.szs to use!");
                }
                else if (arg.EndsWith(".szs"))
                    CreateProbesFromCourseModel(arg);
                else if (Directory.Exists(arg)) //done by folder
                {
                    string path = Path.Combine(arg, "course_model.szs");
                    if (File.Exists(path)) 
                        CreateProbesFromCourseModel(path);
                }
            }
        }

        static void CreateProbesFromCourseModel(string path)
        {
            string settings_folder = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);

            Console.WriteLine($"Loading bfres {Path.GetFileName(path)} to create bounding region");

            var resFile = new ResFile(new MemoryStream(YAZ0.Decompress(path)));
            string folder = Path.GetDirectoryName(path); //folder to save bglpbd to

            var settings = ProbeGenerator.Settings.Load(settings_folder);

            Console.WriteLine($"Creating probes with color {settings.Color}");

            var tool = new ProbeGenerator(resFile.IsPlatformSwitch);
            tool.SetupProbes(resFile, settings);

            if (resFile.IsPlatformSwitch)
                tool.SaveCompressed(Path.Combine(folder, "course_bglpbd.szs"));
            else
                tool.Save(Path.Combine(folder, "course.bglpbd"));
        }
    }
}