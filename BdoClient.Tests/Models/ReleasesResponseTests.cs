using System.Text.Json;
using BdoClient.Models;

namespace BdoClient.Tests.Models;

public class ReleasesResponseTests
{
    private const string SampleJson = """
    {
        "success": true,
        "generated_at": "2026-08-13T02:27:08+03:00",
        "data": {
            "official_patch": 396,
            "official_patch_checked_at": "2026-08-12T15:30:32+03:00",
            "official_source_url": "https://naeu-o-dn.playblackdesert.com/UploadData/ads/languagedata_en/396/languagedata_en.loc",
            "filename": "languagedata_en.loc",
            "install_path_patterns": [
                {
                    "pattern": "{drive}:\\Program Files (x86)\\Steam\\steamapps\\common\\Black Desert Online\\ads\\",
                    "launcher": "steam",
                    "description": "Steam"
                }
            ],
            "install_guide_url": "https://bdo-ua.com.ua/download",
            "progress": {
                "total_rows": 969928,
                "translated_percent": 96.8,
                "manual_rows": 20,
                "manual_percent": 0,
                "machine_rows": 939275,
                "machine_percent": 96.8
            },
            "modes": [
                {
                    "slug": "full-ukrainian",
                    "public_name": "Full Ukrainian",
                    "description": "Complete Ukrainian localization",
                    "audience": "All players",
                    "current": {
                        "public_id": "01KZFM8YZBEBYF9JYSACTR8XW9",
                        "version": 2,
                        "filename": "languagedata_en.loc",
                        "download_url": "https://bdo-ua.com.ua/download/releases/01KZFM8YZBEBYF9JYSACTR8XW9",
                        "size_bytes": 38604148,
                        "sha256": "3b2fce8035666a5251878ce434f741dbdcd62574686ae42c87663097546c3ecf",
                        "patch": 396,
                        "compatible_with_official_patch": true,
                        "published_at": "2026-08-08T05:48:00+03:00",
                        "game_tested_at": "2026-08-08T05:47:56+03:00",
                        "game_test": {
                            "state": "known_issues",
                            "label": "Tested",
                            "note": null
                        },
                        "stats": {
                            "rows_in_file": 1394469
                        },
                        "announcements": {
                            "discord_releases": {
                                "sent": false,
                                "sent_at": null
                            },
                            "telegram_main": {
                                "sent": false,
                                "sent_at": null
                            }
                        }
                    },
                    "history": [
                        {
                            "public_id": "01KZCN3VFCR2XFBEGYDBN1Y32P",
                            "version": 2,
                            "patch": 396,
                            "status": "superseded",
                            "published_at": "2026-08-07T02:04:22+03:00",
                            "retired_at": "2026-08-08T05:48:00+03:00"
                        }
                    ]
                }
            ]
        }
    }
    """;

    [Fact]
    public void Deserialize_ValidJson_ReturnsSuccess()
    {
        var response = JsonSerializer.Deserialize<ReleasesResponse>(SampleJson);
        Assert.NotNull(response);
        Assert.True(response!.Success);
        Assert.NotNull(response.Data);
    }

    [Fact]
    public void Deserialize_Mode_HasCurrent()
    {
        var response = JsonSerializer.Deserialize<ReleasesResponse>(SampleJson);
        var mode = response!.Data!.Modes![0];
        Assert.Equal("full-ukrainian", mode.Slug);
        Assert.NotNull(mode.Current);
        Assert.Equal("01KZFM8YZBEBYF9JYSACTR8XW9", mode.Current!.PublicId);
    }

    [Fact]
    public void Deserialize_Current_Null()
    {
        var json = """
        {
            "success": true,
            "data": {
                "modes": [
                    {
                        "slug": "test-mode",
                        "public_name": "Test",
                        "description": "Test",
                        "audience": "Test",
                        "current": null,
                        "history": []
                    }
                ]
            }
        }
        """;
        var response = JsonSerializer.Deserialize<ReleasesResponse>(json);
        var mode = response!.Data!.Modes![0];
        Assert.Null(mode.Current);
    }

    [Fact]
    public void Deserialize_MalformedJson_ThrowsException()
    {
        var json = "not valid json";
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<ReleasesResponse>(json));
    }

    [Fact]
    public void Deserialize_EmptyResponse_ThrowsException()
    {
        var json = "";
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<ReleasesResponse>(json));
    }

    [Fact]
    public void Deserialize_PropertiesMappedCorrectly()
    {
        var response = JsonSerializer.Deserialize<ReleasesResponse>(SampleJson);
        var data = response!.Data!;

        Assert.Equal(396, data.OfficialPatch);
        Assert.Equal("languagedata_en.loc", data.Filename);
        Assert.NotNull(data.OfficialSourceUrl);
        Assert.NotNull(data.InstallGuideUrl);

        var progress = data.Progress!;
        Assert.Equal(969928, progress.TotalRows);
        Assert.Equal(96.8, progress.TranslatedPercent);

        var current = data.Modes![0].Current!;
        Assert.Equal(38604148, current.SizeBytes);
        Assert.Equal("3b2fce8035666a5251878ce434f741dbdcd62574686ae42c87663097546c3ecf", current.Sha256);
        Assert.Equal(396, current.Patch);
        Assert.True(current.CompatibleWithOfficialPatch);

        var history = data.Modes[0].History!;
        Assert.Single(history);
        Assert.Equal("superseded", history[0].Status);
    }
}
