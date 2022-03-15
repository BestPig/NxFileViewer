﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emignatik.NxFileViewer.FileLoading.QuickFileInfoLoading;
using Emignatik.NxFileViewer.Localization;
using Emignatik.NxFileViewer.Services.BackgroundTask;
using Emignatik.NxFileViewer.Services.FileRenaming.Exceptions;
using Emignatik.NxFileViewer.Services.FileRenaming.Models;
using Emignatik.NxFileViewer.Services.FileRenaming.Models.PatternParts;
using Emignatik.NxFileViewer.Services.OnlineServices;
using LibHac.Ncm;
using Microsoft.Extensions.Logging;

namespace Emignatik.NxFileViewer.Services.FileRenaming;

public class FileRenamerService : IFileRenamerService
{
    private readonly IPackageInfoLoader _packageInfoLoader;
    private readonly ICachedOnlineTitleInfoService _cachedOnlineTitleInfoService;

    public FileRenamerService(IPackageInfoLoader packageInfoLoader, ICachedOnlineTitleInfoService cachedOnlineTitleInfoService)
    {
        _packageInfoLoader = packageInfoLoader ?? throw new ArgumentNullException(nameof(packageInfoLoader));
        _cachedOnlineTitleInfoService = cachedOnlineTitleInfoService ?? throw new ArgumentNullException(nameof(cachedOnlineTitleInfoService));
    }

    public async Task RenameFromDirectoryAsync(string inputDirectory, string? fileFilters, bool includeSubdirectories, INamingSettings namingSettings, bool isSimulation, ILogger? logger, IProgressReporter progressReporter, CancellationToken cancellationToken)
    {
        ValidateNamingSettings(namingSettings);

        var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var directoryInfo = new DirectoryInfo(inputDirectory);

        var fileFiltersRegex = fileFilters?.Split(';').Select(filter => new Regex(filter.Replace("*", ".*"), RegexOptions.IgnoreCase | RegexOptions.Singleline)).ToArray();

        var matchingFiles = directoryInfo.GetFiles("*", searchOption).Where(file =>
        {
            return fileFiltersRegex == null || fileFiltersRegex.Any(regex => regex.IsMatch(file.Name));
        }).ToArray();


        logger?.LogInformation(LocalizationManager.Instance.Current.Keys.RenamingTool_LogNbFilesToRename.SafeFormat(matchingFiles.Length));

        var logPrefix = isSimulation ? $"{LocalizationManager.Instance.Current.Keys.RenamingTool_LogSimulationMode}" : "";

        for (var index = 0; index < matchingFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var matchingFile = matchingFiles[index];
            progressReporter.SetText(matchingFile.Name);

            try
            {
                var renamingResult = await RenameFileAsyncInternal(matchingFile, namingSettings, isSimulation, cancellationToken);

                if (renamingResult.IsRenamed)
                {
                    var message = LocalizationManager.Instance.Current.Keys.RenamingTool_LogFileRenamed.SafeFormat(logPrefix, renamingResult.OldFileName, renamingResult.NewFileName);
                    logger?.LogWarning(message);
                }
                else
                {
                    logger?.LogInformation(LocalizationManager.Instance.Current.Keys.RenamingTool_LogFileAlreadyNamedProperly.SafeFormat(logPrefix, renamingResult.OldFileName));
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, LocalizationManager.Instance.Current.Keys.RenamingTool_FailedToRenameFile.SafeFormat(matchingFile.FullName, ex.Message));
            }

            progressReporter.SetPercentage((index + 1) / (double)matchingFiles.Length);
        }
        progressReporter.SetText("");
    }


    public Task<RenamingResult> RenameFileAsync(string inputFile, INamingSettings namingSettings, bool isSimulation, CancellationToken cancellationToken)
    {
        ValidateNamingSettings(namingSettings);
        return RenameFileAsyncInternal(new FileInfo(inputFile), namingSettings, isSimulation, cancellationToken);
    }

    private static void ValidateNamingSettings(INamingSettings namingSettings)
    {
        ValidateInvalidWindowsFileNameChar(namingSettings.InvalidFileNameCharsReplacement);
        ValidateAllowedKeywords(namingSettings.ApplicationPattern, namingSettings.PatchPattern, namingSettings.AddonPattern);
    }

    private static void ValidateAllowedKeywords(IEnumerable<PatternPart> applicationPattern, IEnumerable<PatternPart> patchPattern, IEnumerable<PatternPart> addonPattern)
    {
        if (HasNotAllowedKeyword(applicationPattern, PatternKeywords.GetAllowedApplicationKeywords(), out var firstNotAllowedApplicationKeyword))
            throw new KeywordNotAllowedException(firstNotAllowedApplicationKeyword.Value, PatternType.Application);

        if (HasNotAllowedKeyword(patchPattern, PatternKeywords.GetAllowedPatchKeywords(), out var firstNotAllowedPatchKeyword))
            throw new KeywordNotAllowedException(firstNotAllowedPatchKeyword.Value, PatternType.Patch);

        if (HasNotAllowedKeyword(addonPattern, PatternKeywords.GetAllowedAddonKeywords(), out var firstNotAllowedAddonKeyword))
            throw new KeywordNotAllowedException(firstNotAllowedAddonKeyword.Value, PatternType.Addon);
    }

    private static bool HasNotAllowedKeyword(IEnumerable<PatternPart> patternParts, IEnumerable<PatternKeyword> allowedKeywords, [NotNullWhen(true)] out PatternKeyword? firstNotAllowedKeyword)
    {
        firstNotAllowedKeyword = patternParts
            .OfType<DynamicTextPatternPart>()
            .Select(part => (PatternKeyword?)part.Keyword)
            .FirstOrDefault(keyword => !allowedKeywords.Contains(keyword!.Value));

        return firstNotAllowedKeyword != null;
    }

    private static void ValidateInvalidWindowsFileNameChar(string? invalidFileNameCharsReplacement)
    {
        if (invalidFileNameCharsReplacement == null)
            return;

        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var invalidChar in invalidChars)
        {
            if (invalidFileNameCharsReplacement.Contains(invalidChar))
                throw new BadInvalidFileNameCharReplacementException(invalidFileNameCharsReplacement, invalidChar);
        }
    }

    private async Task<RenamingResult> RenameFileAsyncInternal(FileInfo inputFile, INamingSettings namingSettings, bool isSimulation, CancellationToken cancellationToken)
    {
        string newFileName;

        var packageInfo = _packageInfoLoader.GetPackageInfo(inputFile.FullName);
        var oldFileName = inputFile.Name;

        if (packageInfo.Contents.Count == 1)
        {
            var content = packageInfo.Contents[0];

            IEnumerable<PatternPart> patternParts;
            switch (content.Type)
            {
                case ContentMetaType.Application:
                    patternParts = namingSettings.ApplicationPattern;
                    break;
                case ContentMetaType.Patch:
                    patternParts = namingSettings.PatchPattern;
                    break;
                case ContentMetaType.AddOnContent:
                    patternParts = namingSettings.AddonPattern;
                    break;
                default:
                    throw new ContentTypeNotSupportedException(content.Type);
            }
            newFileName = await ComputePackageFileName(content, packageInfo.AccuratePackageType, patternParts, cancellationToken);
        }
        else
        {
            //TODO: supporter les super NSP/XCI
            throw new SuperPackageNotSupportedException();
        }

        if (namingSettings.ReplaceWhiteSpaceChars)
        {
            var replacement = namingSettings.WhiteSpaceCharsReplacement ?? "";
            var whiteSpaceCharsToRemove = newFileName.Where(char.IsWhiteSpace).Distinct().ToArray();
            foreach (var whiteSpaceChar in whiteSpaceCharsToRemove)
            {
                newFileName = newFileName.Replace(whiteSpaceChar.ToString(), replacement);
            }
        }

        var invalidFileNameChars = Path.GetInvalidFileNameChars();
        var invalidCharReplacement = namingSettings.InvalidFileNameCharsReplacement ?? "";
        foreach (var invalidFileNameChar in invalidFileNameChars)
        {
            newFileName = newFileName.Replace(invalidFileNameChar.ToString(), invalidCharReplacement);
        }

        var shouldBeRenamed = !string.Equals(newFileName, oldFileName);
        if (!isSimulation && shouldBeRenamed)
        {
            var destFileName = Path.Combine(inputFile.DirectoryName!, newFileName);
            inputFile.MoveTo(destFileName, false);
        }

        return new RenamingResult
        {
            IsSimulation = isSimulation,
            IsRenamed = shouldBeRenamed,
            OldFileName = oldFileName,
            NewFileName = newFileName,
        };
    }

    private async Task<string> ComputePackageFileName(Content content, AccuratePackageType accuratePackageType, IEnumerable<PatternPart> patternParts, CancellationToken cancellationToken)
    {
        var newFileName = "";

        foreach (var patternPart in patternParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (patternPart)
            {
                case StaticTextPatternPart staticText:
                    newFileName += staticText.Text;
                    break;
                case DynamicTextPatternPart dynamicText:
                    switch (dynamicText.Keyword)
                    {
                        case PatternKeyword.TitleIdL:
                            newFileName += content.TitleId.ToLower();
                            break;
                        case PatternKeyword.TitleIdU:
                            newFileName += content.TitleId.ToUpper();
                            break;
                        case PatternKeyword.FirstTitleName:
                            var firstTitle = content.NacpData?.Titles.FirstOrDefault();
                            if (firstTitle != null)
                                newFileName += firstTitle.Name;
                            else
                                newFileName += "NO_TITLE";
                            break;
                        case PatternKeyword.PackageTypeL:
                            newFileName += accuratePackageType.ToString().ToLower();
                            break;
                        case PatternKeyword.PackageTypeU:
                            newFileName += accuratePackageType.ToString().ToUpper();
                            break;
                        case PatternKeyword.VersionNum:
                            newFileName += content.Version.Version.ToString();
                            break;
                        case PatternKeyword.OnlineTitleName:
                            var onlineTitleInfo = await _cachedOnlineTitleInfoService.GetTitleInfoAsync(content.TitleId);

                            if (onlineTitleInfo != null)
                                newFileName += onlineTitleInfo.Name;
                            else
                                newFileName += "NO_TITLE";
                            break;
                        default:
                            throw new NotSupportedException($"Unknown application keyword «{dynamicText.Keyword}».");
                    }

                    break;
                default:
                    throw new NotSupportedException($"Unknown part of type «{patternPart.GetType().Name}».");
            }
        }

        return newFileName;
    }


}