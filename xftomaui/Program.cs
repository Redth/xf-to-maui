using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
using Cake.Common;
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
    //public const string BasePath = "C:/xamarin/Xamarin.Forms/"; // "/Users/redth/code/Xamarin.Forms/";
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
        // Clean up repo to start fresh
        context.StartProcess("git", new ProcessSettings{ Arguments = $"-C \"{Consts.BasePath}\" reset --hard" });
        context.StartProcess("git", new ProcessSettings{ Arguments = $"-C \"{Consts.BasePath}\" clean -xdf" });

        ReplaceMappings(context, Consts.NameMappingsXlsx, new [] {
            ("Maui.Essentials", Consts.BasePath + "src/Essentials/**/*.{cs,xaml}" ),
            ("Maui.Compatibility", Consts.BasePath + "src/Platform.Renderers/**/*.{cs,xaml}" ),
            ("Maui.Controls", Consts.BasePath + "src/Forms/**/*.{cs,xaml}" ),
            ("Maui.Controls", Consts.BasePath + "src/Controls/**/*.{cs,xaml}" ),
            ("Maui.Core", Consts.BasePath + "src/Platform.Handlers/**/*.{cs,xaml}" ),
        });


        var directoryMapping = ReadExcel(context, Consts.NameMappingsXlsx, "Directories", true)
            .Select(dm => (dm[0], dm[1]));

        var filenameMapping = ReadExcel(context, Consts.NameMappingsXlsx, "Files", true)
            .Select(dm => (dm[0], dm[1]));

        FixRelativeFileReferences(context, directoryMapping, filenameMapping);

        RenameFiles(context, filenameMapping);

        RenameFolders(context, directoryMapping);

        FixSlns(context, filenameMapping, directoryMapping);
        
        DeleteEmptyDirectories(context, Consts.BasePath);


        if (context.FileExists("./.github/CODEOWNERS"))
            context.DeleteFile("./.github/CODEOWNERS");
    }

    static IEnumerable<(string from, string to)> GetNamespaceMappings(ICakeContext context, FilePath xlsxFile, params string[] sheets)
    {
        var namespaceMapping = new Dictionary<string, string>();

        foreach (var sheet in sheets)
        {
            var setData = ReadExcel(context, xlsxFile, sheet, true);

            foreach (var row in setData)
            {
                var fromNs = row?[0];
                var toNs = row?[1];

                if (!string.IsNullOrEmpty(fromNs) && !string.IsNullOrEmpty(toNs))
                    namespaceMapping.TryAdd(fromNs, toNs);
            }
        }

        var ordered = namespaceMapping.OrderByDescending(kvp => kvp.Key?.Split('.', 1000)?.Length ?? 0);

        return ordered.Select(o => (o.Key, o.Value));
    }

    static void FixRelativeFileReferences(ICakeContext context, IEnumerable<(string from, string to)> directoryMappings, IEnumerable<(string from, string to)> fileMappings)
    {
        var globPattern = Consts.BasePath + "**/*.{csproj,targets,props,shproj,fsproj}";

        foreach (var file in context.GetFiles(globPattern))
        {
            var fileFullPath = PlatformFriendlyPath(context, file);

            if (file.Segments.Contains("ControlGallery"))
                continue;

            var text = context.FileReadText(file);

            var allMatches = new List<Match>();

            allMatches.AddRange(Regex.Matches(text, "Include=\"(?<path>.*?)\"", RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace));
            allMatches.AddRange(Regex.Matches(text, "Project=\"(?<path>.*?)\"", RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace));

            foreach (var m in allMatches)
            {
                var rp = m?.Groups?["path"]?.Value;

                if (!string.IsNullOrEmpty(rp))
                {
                    var refRelativePath = new FilePath(rp);

                    if (file.FullPath.Contains("Maps.macOS"))
                        context.Log.Information("MapsMacos");
                        
                    if (file.Segments.Contains("eng"))
                        continue;
                    if (!context.FileExists(refRelativePath.MakeAbsolute(file.GetDirectory())))
                        continue;

                    // For the Include="" value get the current full path
                    var refFullPath = refRelativePath.MakeAbsolute(file.GetDirectory());

                    // Get the relative directory path for the current value
                    var refDirRelativePath = refFullPath.GetDirectory().FullPath.Replace(Consts.BasePath, "./").TrimEnd('/', '\\');

                    // TODO: the path may  be deeper than the mapping
                    // in other words the path needs to check if it 
                    // starts with the one in the mapping 
                    // then figure out the additional folders

                    // Find a mapping for the directory to see if we are changing it
                    // so we can figure out what the new directory will be and fix the reference
                    var refMapping = directoryMappings.FirstOrDefault(dm =>
                        IsSamePathOrDeeper(refDirRelativePath, dm.from));

                    // See if the reference is further into the folder path than just the mapping
                    // since it's possible the reference is inside of one or more subfolders 
                    // beyond what the mapping specified
                    var refPathSubDirs = new DirectoryPath("./");
                    
                    if (rp.Contains("Xamarin.Forms.Platform.MacOS.csproj")
                        )
                        context.Log.Information("..\\Xamarin.Forms.Core\\Xamarin.Forms.Core.csproj");


                    if (refMapping != default)
                    {
                        var fromBase = new DirectoryPath(Consts.BasePath).Combine(refMapping.from);
                        refPathSubDirs = fromBase.GetRelativePath(refFullPath.GetDirectory());
                    }

                    // We only care about SUB-dirs, deeper than the original reference's path
                    // check if the relative difference would have us backing out to a higher path
                    // and ignore those
                    //if (refPathSubDirs.FullPath.StartsWith(".."))
                    //    refPathSubDirs = new DirectoryPath("./");
                        
                    var currentFilename = refFullPath.GetFilename();

                    var refFileMapping = fileMappings.FirstOrDefault(fm => fm.from.Equals(currentFilename.FullPath));

                    var renamedFilename = refFileMapping != default ? refFileMapping.to : currentFilename;


                    // The reference might have its file renamed
                    var newRefFull = refFullPath.GetDirectory().Combine(refPathSubDirs).CombineWithFilePath(renamedFilename);

                    // The reference also be getting moved to another folder
                    if (refMapping != default)
                        newRefFull = (new DirectoryPath(Consts.BasePath)).Combine(refMapping.to).Combine(refPathSubDirs).CombineWithFilePath(renamedFilename);

                    // Get the new file location path but relative to the base working dir
                    var newFileRelativePath = file.GetDirectory().FullPath.Replace(Consts.BasePath, "./").TrimEnd('/', '\\');
                    // Look up a mapping for where the new file location directory would be to see if it's also moving
                    var thisFileMapping = directoryMappings.FirstOrDefault(dm => dm.from.TrimEnd('/', '\\') == newFileRelativePath);
            
                    // By default if no mapping is found, it means the file's dir isn't moving so we can use
                    // a relative path to the new reference path for the existing file's location
                    var newRefRelativeToFile = file;

                    // If this file is also moving to a new folder, we need to make the new reference's relative path
                    // be relative to the NEW location that this file will be in
                    if (thisFileMapping != default)
                        newRefRelativeToFile = (new DirectoryPath(Consts.BasePath)).Combine(thisFileMapping.to).CombineWithFilePath(file.GetFilename());

                    var finalnewref = newRefRelativeToFile.GetRelativePath(newRefFull);

                    text = text.Replace(rp, MSBuildifyPath(finalnewref));
                }
            }

            context.FileWriteText(file, text);
        }
    }

    static void FixSlns(ICakeContext context, IEnumerable<(string from, string to)> fileMappings, IEnumerable<(string from, string to)> dirMappings)
    {
        var slnFiles = context.GetFiles(Consts.BasePath + "**/*.sln");

        foreach (var sln in slnFiles)
        {
            var text = context.FileReadText(sln);

            foreach (var dm in dirMappings)
            {
                var fromFormatted = dm.from.TrimStart('.', '/').Replace('/', '\\');
                var toFormatted = dm.to.TrimStart('.', '/').Replace('/', '\\');

                text = text.Replace(fromFormatted, toFormatted);
            }

            foreach (var fm in fileMappings)
            {
                text = text.Replace(fm.from, fm.to);
                text = text.Replace(System.IO.Path.GetFileNameWithoutExtension(fm.from), System.IO.Path.GetFileNameWithoutExtension(fm.to));
            }

            text = text.Replace("Platform.Handlers", "Platform.Handlers");
            text = text.Replace("\"Forms\"", "\"Forms\"");

            context.FileWriteText(sln, text);
        }
    }

    static void RenameFolders(ICakeContext context, IEnumerable<(string from, string to)> mappings)
    {
        var baseDir = new DirectoryPath(Consts.BasePath);

        foreach (var map in mappings)
            MoveDirectory(context, baseDir.Combine(map.from), baseDir.Combine(map.to));

        foreach (var map in mappings)
        {
            if (context.DirectoryExists(map.from))
                context.DeleteDirectory(map.from, new DeleteDirectorySettings { Recursive =true, Force = true });
        }
    }

    // ie:  path: './src/Folder/subfolder/file.txt', sameOrDeeperThan: './src/Folder'
    static bool IsSamePathOrDeeper(DirectoryPath path, DirectoryPath sameOrDeeperThan)
    {
        // If it's not at least as deep as the same or deeper, obvious false
        if (path.Segments.Length < sameOrDeeperThan.Segments.Length)
            return false;

        for (int i = 0; i < path.Segments.Length; i++)
        {
            // If we make it to where all segments matched so far
            // and we've compared all the segments in the sameOrDeeperThan
            // then we are deeper
            if (i >= sameOrDeeperThan.Segments.Length)
                return true;

            // If the current segments don't match, it's not the same path
            if (path.Segments[i] != sameOrDeeperThan.Segments[i])
                return false;
        }

        return true;
    }

    static string MSBuildifyPath(Path path)
    {
        return path.FullPath.Replace("/./", "/").Replace('/', '\\');
    }

    static string PlatformFriendlyPath(ICakeContext context, Path path)
    {
        if (context.Environment.Platform.Family == PlatformFamily.Windows)
            return  path.FullPath.Replace('/', '\\');
        return path.FullPath;
    }

    public static void MoveDirectory(ICakeContext context, DirectoryPath sourceDir, DirectoryPath targetDir)
    {
        var sourcePath = PlatformFriendlyPath(context, sourceDir).TrimEnd('\\', '/', ' ');
        var targetPath = PlatformFriendlyPath(context, targetDir).TrimEnd('\\', '/', ' ');
        var files = System.IO.Directory.EnumerateFiles(sourcePath, "*", System.IO.SearchOption.AllDirectories)
                            .GroupBy(s=> System.IO.Path.GetDirectoryName(s));
        foreach (var folder in files)
        {
            var targetFolder = folder.Key.Replace(sourcePath, targetPath);
            System.IO.Directory.CreateDirectory(targetFolder);
            foreach (var file in folder)
            {
                var targetFile = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(file));
                if (System.IO.File.Exists(targetFile))
                    System.IO.File.Delete(targetFile);
                System.IO.File.Move(file, targetFile);
            }
        }
        //System.IO.Directory.Delete(source, true);
    }

    static void DeleteEmptyDirectories(ICakeContext context, DirectoryPath root)
    {
        foreach (var directory in context.GetSubDirectories(root))
        {
            DeleteEmptyDirectories(context, directory);

            var path = PlatformFriendlyPath(context, directory);
            if (System.IO.Directory.GetFiles(path).Length == 0 && 
                System.IO.Directory.GetDirectories(path).Length == 0)
            {
                System.IO.Directory.Delete(path, false);
            }
        }
    }

    static void RenameFiles(ICakeContext context, IEnumerable<(string from, string to)> mappings)
    {
        var files = context.GetFiles(Consts.BasePath + "**/*");

        foreach (var file in files)
        {
            var filename = System.IO.Path.GetFileName(PlatformFriendlyPath(context, file));

            var map = mappings.FirstOrDefault(m => m.from.Equals(filename));

            if (map != default)
                context.MoveFile(file, file.GetDirectory().CombineWithFilePath(map.to));
        }
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

        using(var package = new ExcelPackage(new System.IO.FileInfo(PlatformFriendlyPath(context, xlsxFile))))
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
