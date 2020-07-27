using System;
using System.Threading.Tasks;

namespace RazorEngineCoreConsoleApp
{
    public class TestModel
    {
        public string Name { get; set; }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            var engine = new RazorEngine();

            try
            {
                engine.AddTemplate("Test1", "Hello @Model.Name 1!");
                engine.AddTemplate("Test2", "Hello @Model.Name 2!");

                var model1 = new { Name = "Raphael Basso" };

                await engine.RunAsync("Test1", model1, Console.Out);
                Console.WriteLine();
                await engine.RunAsync("Test2", model1, Console.Out);
                Console.WriteLine();

                Console.WriteLine(await engine.RunAsync("Test1", model1));
                Console.WriteLine(await engine.RunAsync("Test2", model1));

                engine.AddTemplate<TestModel>("Test3", "Hello @Model.Name 3!");
                engine.AddTemplate<TestModel>("Test4", "Hello @Model.Name 4!");

                var model2 = new TestModel { Name = "Raphael Basso" };

                await engine.RunAsync("Test3", model2, Console.Out);
                Console.WriteLine();
                await engine.RunAsync("Test4", model2, Console.Out);
                Console.WriteLine();

                Console.WriteLine(await engine.RunAsync("Test3", model2));
                Console.WriteLine(await engine.RunAsync("Test4", model2));

                var model3 = new TestModel { Name = "Raphael Basso" };

                await engine.RunCompileAsync("Test5", "Hello @Model.Name 5!", model3, Console.Out);
                Console.WriteLine();
                await engine.RunCompileAsync("Test6", "Hello @Model.Name 6!", model3, Console.Out);
                Console.WriteLine();
            }

            catch (TemplateCompilationException e)
            {
                foreach (var diagnostic in e.Diagnostics)
                {
                    Console.WriteLine(diagnostic);
                }
            }
        }
    }
}
