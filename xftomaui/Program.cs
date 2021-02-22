using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
using Cake.Common.IO;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.FileHelpers;
using Cake.Frosting;
using System;
using OfficeOpenXml;

public static class Consts
{
    public const string BasePath = "/Users/redth/code/Xamarin.Forms/";
    public const string NameMappingsXlsx = "./NameMappings.xlsx";
}

public static class Program
{
    public static int Main(string[] args)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        return new CakeHost()
            .UseContext<BuildContext>()
            .Run(args);
    }
}

public class BuildContext : FrostingContext
{
    public bool Delay { get; set; }

    public BuildContext(ICakeContext context)
        : base(context)
    {
        Delay = context.Arguments.HasArgument("delay");
    }
}

[TaskName("Default")]
public sealed class DefaultTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        // var controlsNamespaces = new List<string>();
        // controlsNamespaces.AddRange(GetNamespaceCodeDeclarations(context, Consts.BasePath + "src/Controls/**/*.cs"));
        // controlsNamespaces.AddRange(GetNamespaceCodeDeclarations(context, Consts.BasePath + "src/Forms/**/*.cs"));
        // context.FileWriteLines("./maui.controls.namespaces.txt", controlsNamespaces.ToArray());

        // context.FileWriteLines("./maui.compat.namespaces.txt",
        //     GetNamespaceCodeDeclarations(context, Consts.BasePath + "src/Platform.Renderers/**/*.cs").ToArray());

        // context.FileWriteLines("./maui.core.namespaces.txt",
        //     GetNamespaceCodeDeclarations(context, Consts.BasePath + "src/Platform.Handlers/**/*.cs").ToArray());


        // context.FileWriteLines("./maui.essentials.namespaces.txt",
        //     GetNamespaceCodeDeclarations(context, Consts.BasePath + "src/Essentials/**/*.cs").ToArray());


        // var csprojFiles = context.GetFiles(Consts.BasePath + "**/*.csproj");
        // context.FileWriteLines("./maui.csproj.names.txt", csprojFiles.Select(p => System.IO.Path.GetFileName(p.FullPath)).ToArray ());
        



        var dirNames = new List<string>();

        foreach (var dir in context.GetDirectories(Consts.BasePath + "**"))
        {
            var rp = dir.GetRelativePath(new DirectoryPath(Consts.BasePath));

            if (dir.Segments.Contains("ControlGallery"))
                continue;

            if (!dirNames.Contains(dir.FullPath))
                dirNames.Add(dir.FullPath);
        }

        context.FileWriteLines("./maui.directories.txt", dirNames.ToArray ());
        

        var directoryMapping = ReadExcel(context, Consts.NameMappingsXlsx, "Directories", true)
            .Select(dm => (dm[0], dm[1]));

        FixRelativeFileReferences(context, directoryMapping);



        return;

        ReplaceMappings(context, Consts.NameMappingsXlsx, new [] {
            ("Maui.Essentials", $"{Consts.BasePath}src/Essentials/**/*.cs" ),
            ("Maui.Compatibility", $"{Consts.BasePath}src/Platform.Renderers/**/*.cs" ),
            ("Maui.Controls", $"{Consts.BasePath}src/Forms/**/*.cs" ),
            ("Maui.Controls", $"{Consts.BasePath}src/Controls/**/*.cs" ),
            ("Maui.Core", $"{Consts.BasePath}src/Platform.Handlers/**/*.cs" ),
        });
    }


    static void FixRelativeFileReferences(ICakeContext context, IEnumerable<(string from, string to)> directoryMappings)
    {
        var globPattern = Consts.BasePath + "**/*.{csproj,targets,props,shproj,fsproj}";

        foreach (var file in context.GetFiles(globPattern))
        {
            var fileFullPath = file.FullPath;

            if (file.Segments.Contains("ControlGallery"))
                continue;

            var text = context.FileReadText(file);

            var matches = Regex.Matches(text, "Include=\"(?<path>.*?)\"", RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace);

            foreach (Match m in matches)
            {
                var rp = m?.Groups?["path"]?.Value;

                if (!string.IsNullOrEmpty(rp))
                {
                    var refRelativePath = new FilePath(rp);

                    if (file.Segments.Contains("eng"))
                        continue;
                    if (!context.FileExists(refRelativePath.MakeAbsolute(file.GetDirectory())))
                        continue;

                    // For the Include="" value get the current full path
                    var refFullPath = refRelativePath.MakeAbsolute(file.GetDirectory());

                    // Get the relative directory path for the current value
                    var refDirRelativePath = refFullPath.GetDirectory().FullPath.Replace(Consts.BasePath, "./").TrimEnd('/', '\\');

                    // Find a mapping for the directory to see if we are changing it
                    // so we can figure out what the new directory will be and fix the reference
                    var refMapping = directoryMappings.FirstOrDefault(dm => dm.from.TrimEnd('/', '\\') == refDirRelativePath);

                    if (refMapping != default)
                    {
                        // Get the full path of where the referenced file will be after we rename/move dirs
                        var newRefFull = (new DirectoryPath(Consts.BasePath)).Combine(refMapping.to).CombineWithFilePath(refRelativePath.GetFilename());

                        // Get the new file location path but relative to the base working dir
                        var newFileRelativePath = file.GetDirectory().FullPath.Replace(Consts.BasePath, "./").TrimEnd('/', '\\');
                        // Look up a mapping for where the new file location directory would be to see if it's also moving
                        var fileMapping = directoryMappings.FirstOrDefault(dm => dm.from.TrimEnd('/', '\\') == newFileRelativePath);
                
                        // By default if no mapping is found, it means the file's dir isn't moving so we can use
                        // a relative path to the new reference path for the existing file's location
                        var newRefRelativeToFile = file;

                        // If this file is also moving to a new folder, we need to make the new reference's relative path
                        // be relative to the NEW location that this file will be in
                        if (fileMapping != default)
                            newRefRelativeToFile = (new DirectoryPath(Consts.BasePath)).Combine(fileMapping.to).CombineWithFilePath(file.GetFilename());

                        var finalnewref = newRefFull.GetRelativePath(newRefRelativeToFile);

                        text = text.Replace(rp, finalnewref.FullPath);

                    }
                }
            }
        }
       

    }


    static void MoveFiles(DirectoryPath newLoc)
    {

    }

    static void ReplaceMappings(ICakeContext context, FilePath xlsxFile, params (string sheetName, string fileGlobPattern)[] sets)
    {
        foreach (var set in sets)
        {
            var setData = ReadExcel(context, xlsxFile, set.sheetName, true);

            var namespaceMapping = new Dictionary<string, string>();

            foreach (var row in setData)
            {
                var fromNs = row?[0];
                var toNs = row?[1];

                if (!string.IsNullOrEmpty(fromNs) && !string.IsNullOrEmpty(toNs))
                    namespaceMapping.TryAdd(fromNs, toNs);
            }

            foreach (var file in context.GetFiles(set.fileGlobPattern))
            {
                var text = context.FileReadText(file);

                foreach (var mapping in namespaceMapping)
                {
                    text = text.Replace(mapping.Key, mapping.Value);
                }
                
                context.FileWriteText(file, text);
            }
        }
    }

    static IEnumerable<List<string>> ReadExcel(ICakeContext context, FilePath xlsxFile, string sheetName, bool hasHeader = true)
    {
        var result = new List<List<string>>();

        using(var package = new ExcelPackage(new System.IO.FileInfo(xlsxFile.FullPath)))
        {
            var sheet = package.Workbook.Worksheets[sheetName];

            for (int row = hasHeader ? 2 : 1; row <= sheet.Dimension.Rows; row++)
            {
                var rowValues = new List<string>();

                for (int col = 1; col <= sheet.Dimension.Columns; col++)
                    rowValues.Add(sheet.GetValue<string>(row, col));

                result.Add(rowValues);
            }
        }

        return result;
    }

    static IEnumerable<string> GetNamespaceCodeDeclarations(ICakeContext context, string glob)
    {
        var csFiles = context.GetFiles(glob);

        var namespaces = new List<string>();

        foreach (var f in csFiles)
        {
            if (f.Segments.Contains("ControlGallery"))
                continue;

            var cs = context.FileReadText(f);


            var matchGroups = context.FindRegexMatchesGroupsInFile(f, "namespace\\s+(?<ns>Xamarin(.*))\\s?{", 
                RegexOptions.IgnorePatternWhitespace | RegexOptions.Multiline);

            foreach (var mg in matchGroups)
            {
                var nsg = mg?.FirstOrDefault(mgi => mgi.Name == "ns");

                var ns = nsg?.Value;

                if (!string.IsNullOrEmpty(ns) && !namespaces.Any(n => n.Equals(ns, StringComparison.OrdinalIgnoreCase)))
                    namespaces.Add(ns);
            }
        }


        var sortedNamespaces = namespaces.OrderByDescending(n => n.Split('.', 1000, StringSplitOptions.RemoveEmptyEntries)?.Length ?? 0);

        return sortedNamespaces;
    }
}
