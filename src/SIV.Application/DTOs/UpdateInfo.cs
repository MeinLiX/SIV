namespace SIV.Application.DTOs;

public sealed record UpdateInfo(
    string NewVersion,
    string CurrentVersion,
    string DownloadUrl,
    string ExpectedHash,
    string ReleaseNotes);
