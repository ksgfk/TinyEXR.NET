using CppSharp;
using CppSharp.AST;
using CppSharp.Generators;
using CppSharp.Passes;

namespace TinyEXR.Generate
{
    internal static class Program
    {
        private class TinyExrLibrary : ILibrary
        {
            public string Root { get; }

            public TinyExrLibrary(string path) { Root = path ?? throw new ArgumentNullException(nameof(path)); }

            public void Setup(Driver driver)
            {
                var options = driver.Options;
                options.GeneratorKind = GeneratorKind.CSharp;
                options.MarshalCharAsManagedChar = false;
                var module = options.AddModule("TinyExrNative");
                module.IncludeDirs.Add(Root);
                module.Headers.Add("TinyEXR.Native.h");
                module.LibraryDirs.Add(Path.Combine(Root, "build", "Release"));
                module.Libraries.Add("TinyEXR.Native.dll");
            }

            public void Postprocess(Driver driver, ASTContext ctx)
            {
                //ctx.SetClassAsValueType("TEXRImage");
                //ctx.SetClassAsValueType("TEXRHeader");
                //ctx.SetClassAsValueType("TEXRVersion");
                //ctx.SetClassAsValueType("TDeepImage");
                //ctx.SetClassAsValueType("TEXRVersion");
                //ctx.SetClassAsValueType("TEXRAttribute");
                //ctx.SetClassAsValueType("TEXRChannelInfo");
                //ctx.SetClassAsValueType("TEXRTile");
                //ctx.SetClassAsValueType("TEXRBox2i");
                //ctx.SetClassAsValueType("TEXRMultiPartHeader");
                //ctx.SetClassAsValueType("TEXRMultiPartImage");
            }

            public void Preprocess(Driver driver, ASTContext ctx)
            {
            }

            public void SetupPasses(Driver driver)
            {
                driver.Context.TranslationUnitPasses.RenameDeclsUpperCase(RenameTargets.Any);
            }
        }

        private static void Main(string[] args)
        {
            string path = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "TinyEXR.Native"));
            ConsoleDriver.Run(new TinyExrLibrary(path));
        }
    }
}
