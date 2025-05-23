﻿using MetadataExtractor;

bool isDryRun = false;
string inputDirectoryPath = string.Empty;
string outputDirectoryPath = string.Empty;
string defaultOutputDirectoryPath = Path.Combine(Environment.CurrentDirectory, "output");
const string exifDirectoryName = "Exif IFD0";
const string exifSubDirectoryName = "Exif SubIFD";
const string quickTimeDirectoryName = "QuickTime Movie Header"; //Modified
const string fileDirectoryName = "File";

foreach (var arg in args)
{
    switch (arg)
    {
        case "-d":
        case "--dry-run":
            isDryRun = true;
            break;
        case "-i":
        case "--input":
            inputDirectoryPath = args[Array.IndexOf(args, arg) + 1];
            break;
        case "-o":
        case "--output":
            outputDirectoryPath = args[Array.IndexOf(args, arg) + 1];
            break;
        default:
            Console.WriteLine($"Unknown argument: {arg}");
            break;
    }
}

var extensions = new[] { ".jpg", ".jpeg", ".mp4", ".mov", ".png", ".gif", ".3gp", ".avi", ".mkv", ".webm", ".wmv", ".flv", ".m4v", ".heic", ".heif", ".tiff", ".bmp", ".raw" };

if (string.IsNullOrEmpty(inputDirectoryPath))
{
    inputDirectoryPath = Path.Combine(Environment.CurrentDirectory, "test-data");
}
if (string.IsNullOrEmpty(outputDirectoryPath))
{
    outputDirectoryPath = defaultOutputDirectoryPath;
}

var files = System.IO.Directory.GetFiles(inputDirectoryPath, "*.*", SearchOption.AllDirectories)
    .Where(file => extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
    .ToList();

Console.WriteLine($"Found {files.Count} files in {inputDirectoryPath}");

foreach (var file in files)
{
    var fileInfo = new FileInfo(file);
    var fileDate = fileInfo.CreationTime;
    Console.WriteLine($"File: {file}{Environment.NewLine}\tDate: {fileDate}");
    var directories = ImageMetadataReader.ReadMetadata(file);
    var directoryName = fileInfo.DirectoryName!;
    var year_month = directoryName.Split(Path.DirectorySeparatorChar).TakeLast(2).Select(_ => int.Parse(_)).ToArray();
    var pathDateTime = new DateTime(year_month[0], year_month[1], 1);
    var metadataTime = GetMetadataDateTime(directories, pathDateTime);
    if (metadataTime != null && metadataTime < fileDate)
    {
        Console.WriteLine($"\tMetadata Date: {metadataTime}");
        if (!isDryRun)
        {
            SaveFileWithNewDateTime(file, metadataTime.Value);
        }
    }
    else
    {
        Console.WriteLine("\tNo metadata date found or it is not earlier than file date.");

        Console.WriteLine($"\tUsing path date: {pathDateTime}");
        if (!isDryRun)
        {
            ChangeMetadataDateTime(file, pathDateTime);
            SaveFileWithNewDateTime(file, pathDateTime, true);
        }
    }
}

void ChangeMetadataDateTime(string filePath, DateTime dateTime)
{
    var directories = ImageMetadataReader.ReadMetadata(filePath);
    var exifDirectory = directories.OfType<MetadataExtractor.Formats.Exif.ExifDirectoryBase>().FirstOrDefault();
    var exifSubDirectory = directories.OfType<MetadataExtractor.Formats.Exif.ExifSubIfdDirectory>().FirstOrDefault();
    var quickTimeDirectory = directories.OfType<MetadataExtractor.Formats.QuickTime.QuickTimeMovieHeaderDirectory>().FirstOrDefault();
    var fileDirectory = directories.OfType<MetadataExtractor.Formats.FileSystem.FileMetadataDirectory>().FirstOrDefault();

    if (exifSubDirectory != null)
    {
        exifSubDirectory.Set(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagDateTimeOriginal, dateTime);
        exifSubDirectory.Set(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagDateTimeDigitized, dateTime);
    }

    if (exifDirectory != null)
    {
        exifDirectory.Set(MetadataExtractor.Formats.Exif.ExifDirectoryBase.TagDateTime, dateTime);
    }

    if (quickTimeDirectory != null)
    {
        quickTimeDirectory.Set(MetadataExtractor.Formats.QuickTime.QuickTimeMovieHeaderDirectory.TagCreated, dateTime);
        quickTimeDirectory.Set(MetadataExtractor.Formats.QuickTime.QuickTimeMovieHeaderDirectory.TagModified, dateTime);
    }

    if (fileDirectory != null)
    {
        fileDirectory.Set(MetadataExtractor.Formats.FileSystem.FileMetadataDirectory.TagFileModifiedDate, dateTime);
    }
}

void SaveFileWithNewDateTime(string filePath, DateTime dateTime, bool hardRewrite = false)
{
    var fileInfo = new FileInfo(filePath);
    var newFileName = $"{dateTime:yyyy-MM-dd}_{fileInfo.Name}";
    var newFilePath = Path.Combine(outputDirectoryPath, newFileName);
    Console.WriteLine($"\tRenaming to: {newFilePath}");
    System.IO.Directory.CreateDirectory(outputDirectoryPath);
    File.Copy(filePath, newFilePath, true);

    if (hardRewrite)
        SetExifCreateDateFromFileModDate(newFilePath, dateTime);

    File.SetCreationTime(newFilePath, dateTime);
    File.SetLastWriteTime(newFilePath, dateTime);
    File.SetLastAccessTime(newFilePath, dateTime);
}

void SetExifCreateDateFromFileModDate(string filePath, DateTime dateTime)
{
    var modDate = dateTime.ToString("yyyy:MM:dd HH:mm:ss");
    var command = $"exiftool -overwrite_original -CreateDate='{modDate}' -ModifyDate='{modDate}' '{filePath}'";

    var process = new System.Diagnostics.Process
    {
        StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }
    };

    process.Start();
    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (!string.IsNullOrEmpty(output))
        Console.WriteLine(output);
    if (!string.IsNullOrEmpty(error))
        Console.WriteLine($"Error: {error}");
}

DateTime? GetMetadataDateTime(IReadOnlyList<MetadataExtractor.Directory> directories, DateTime pathDateTime)
{
    var exifDirectory = directories.OfType<MetadataExtractor.Formats.Exif.ExifDirectoryBase>().FirstOrDefault();
    var exifSubDirectory = directories.OfType<MetadataExtractor.Formats.Exif.ExifSubIfdDirectory>().FirstOrDefault();
    var quickTimeDirectory = directories.OfType<MetadataExtractor.Formats.QuickTime.QuickTimeMovieHeaderDirectory>().FirstOrDefault();
    var fileDirectory = directories.OfType<MetadataExtractor.Formats.FileSystem.FileMetadataDirectory>().FirstOrDefault();

    if (exifSubDirectory != null)
    {
        var dateTimeOriginalTag = exifSubDirectory.Tags.FirstOrDefault(t => t.Name == "Date/Time Original");
        if (exifSubDirectory.TryGetDateTime(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagDateTimeOriginal, out var dateTimeOriginal))
        {
            if (dateTimeOriginal.Year == pathDateTime.Year && dateTimeOriginal.Month == pathDateTime.Month)
                return dateTimeOriginal;
        }

        if (exifSubDirectory.TryGetDateTime(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagDateTimeDigitized, out var dateTimeDigitized))
        {
            if (dateTimeDigitized.Year == pathDateTime.Year && dateTimeDigitized.Month == pathDateTime.Month)
                return dateTimeDigitized;
        }
    }

    if (exifDirectory != null)
    {
        var dateTimeTag = exifDirectory.Tags.FirstOrDefault(t => t.Name == "Date/Time");
        
        if (exifDirectory.TryGetDateTime( MetadataExtractor.Formats.Exif.ExifDirectoryBase.TagDateTime, out var dateTime))
        {
            if (dateTime.Year == pathDateTime.Year && dateTime.Month == pathDateTime.Month)
                return dateTime;
        }
    }

    if (exifDirectory != null)
    {
        var dateTimeTag = exifDirectory.Tags.FirstOrDefault(t => t.Name == "Date/Time");
        
        if (exifDirectory.TryGetDateTime( MetadataExtractor.Formats.Exif.ExifDirectoryBase.TagDateTime, out var dateTime))
        {
            return dateTime;
        }
    }

    if (quickTimeDirectory != null)
    {
        if (quickTimeDirectory.TryGetDateTime(MetadataExtractor.Formats.QuickTime.QuickTimeMovieHeaderDirectory.TagModified, out var modificationDate))
        {
            if (modificationDate.Year == pathDateTime.Year && modificationDate.Month == pathDateTime.Month)
                return modificationDate;
        }

        if (quickTimeDirectory.TryGetDateTime(MetadataExtractor.Formats.QuickTime.QuickTimeMovieHeaderDirectory.TagCreated, out var creationDate))
        {
            if (creationDate.Year == pathDateTime.Year && creationDate.Month == pathDateTime.Month)
                return creationDate;
        }
    }

    if (fileDirectory != null)
    {
        if (fileDirectory.TryGetDateTime(MetadataExtractor.Formats.FileSystem.FileMetadataDirectory.TagFileModifiedDate, out var fileModifiedDate))
        {
            if (fileModifiedDate.Year == pathDateTime.Year && fileModifiedDate.Month == pathDateTime.Month)
                return fileModifiedDate;
        }
    }

    return null;
}