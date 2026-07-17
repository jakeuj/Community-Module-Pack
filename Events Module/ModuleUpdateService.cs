using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Events_Module {
    internal sealed class ModuleReleaseVersion : IComparable<ModuleReleaseVersion>, IEquatable<ModuleReleaseVersion> {
        private static readonly Regex ManifestVersionPattern = new Regex(
            @"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-fork\.(?<fork>0|[1-9]\d*))?$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled
        );
        private static readonly Regex ReleaseTagPattern = new Regex(
            @"^events-zh-tw-v(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)-fork\.(?<fork>0|[1-9]\d*)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled
        );

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public int Fork { get; }
        public bool HasFork { get; }

        private ModuleReleaseVersion(int major, int minor, int patch, int fork, bool hasFork) {
            Major = major;
            Minor = minor;
            Patch = patch;
            Fork = fork;
            HasFork = hasFork;
        }

        public static bool TryParseManifestVersion(string value, out ModuleReleaseVersion version) {
            return TryParse(value, ManifestVersionPattern, out version);
        }

        public static bool TryParseReleaseTag(string value, out ModuleReleaseVersion version) {
            return TryParse(value, ReleaseTagPattern, out version);
        }

        private static bool TryParse(string value, Regex pattern, out ModuleReleaseVersion version) {
            version = null;
            if (string.IsNullOrWhiteSpace(value)) return false;

            Match match = pattern.Match(value);
            if (!match.Success) return false;

            if (!int.TryParse(match.Groups["major"].Value, out int major) ||
                !int.TryParse(match.Groups["minor"].Value, out int minor) ||
                !int.TryParse(match.Groups["patch"].Value, out int patch)) {
                return false;
            }

            bool hasFork = match.Groups["fork"].Success;
            int fork = 0;
            if (hasFork && !int.TryParse(match.Groups["fork"].Value, out fork)) return false;

            version = new ModuleReleaseVersion(major, minor, patch, fork, hasFork);
            return true;
        }

        public int CompareTo(ModuleReleaseVersion other) {
            if (ReferenceEquals(other, null)) return 1;

            int comparison = Major.CompareTo(other.Major);
            if (comparison != 0) return comparison;
            comparison = Minor.CompareTo(other.Minor);
            if (comparison != 0) return comparison;
            comparison = Patch.CompareTo(other.Patch);
            return comparison != 0 ? comparison : Fork.CompareTo(other.Fork);
        }

        public bool Equals(ModuleReleaseVersion other) {
            return !ReferenceEquals(other, null) && CompareTo(other) == 0;
        }

        public override bool Equals(object obj) {
            return Equals(obj as ModuleReleaseVersion);
        }

        public override int GetHashCode() {
            unchecked {
                int hash = Major;
                hash = (hash * 397) ^ Minor;
                hash = (hash * 397) ^ Patch;
                return (hash * 397) ^ Fork;
            }
        }

        public override string ToString() {
            string baseVersion = $"{Major}.{Minor}.{Patch}";
            return HasFork ? $"{baseVersion}-fork.{Fork}" : baseVersion;
        }
    }

    internal enum ModuleUpdateFailure {
        None,
        InvalidCurrentVersion,
        InvalidRelease,
        MissingAsset,
        InvalidDigest,
        InvalidAssetUrl
    }

    internal sealed class ModuleUpdateRelease {
        public ModuleReleaseVersion Version { get; }
        public string TagName { get; }
        public string AssetUrl { get; }
        public string Sha256 { get; }

        public ModuleUpdateRelease(ModuleReleaseVersion version, string tagName, string assetUrl, string sha256) {
            Version = version;
            TagName = tagName;
            AssetUrl = assetUrl;
            Sha256 = sha256;
        }
    }

    internal sealed class ModuleUpdateCheckResult {
        public ModuleReleaseVersion CurrentVersion { get; }
        public ModuleUpdateRelease Release { get; }
        public ModuleUpdateFailure Failure { get; }
        public bool UpdateAvailable => Failure == ModuleUpdateFailure.None && Release != null && Release.Version.CompareTo(CurrentVersion) > 0;

        private ModuleUpdateCheckResult(ModuleReleaseVersion currentVersion, ModuleUpdateRelease release, ModuleUpdateFailure failure) {
            CurrentVersion = currentVersion;
            Release = release;
            Failure = failure;
        }

        public static ModuleUpdateCheckResult Failed(ModuleUpdateFailure failure, ModuleReleaseVersion currentVersion = null) {
            return new ModuleUpdateCheckResult(currentVersion, null, failure);
        }

        public static ModuleUpdateCheckResult Checked(ModuleReleaseVersion currentVersion, ModuleUpdateRelease release) {
            return new ModuleUpdateCheckResult(currentVersion, release, ModuleUpdateFailure.None);
        }
    }

    internal static class ModuleUpdatePolicy {
        public static bool ShouldAutomaticallyInstall(
            bool autoInstallRequested,
            bool autoUpdateEnabled,
            bool installSupported,
            bool updateAvailable
        ) {
            return autoInstallRequested && autoUpdateEnabled && installSupported && updateAvailable;
        }
    }

    internal sealed class ModuleUpdateService : IDisposable {
        internal const string LatestReleaseEndpoint = "https://api.github.com/repos/jakeuj/Community-Module-Pack/releases/latest";
        internal const string AssetName = "Events.Module.bhm";
        internal const int RequestTimeoutSeconds = 10;

        private static readonly Regex DigestPattern = new Regex(
            @"^sha256:(?<hash>[0-9a-fA-F]{64})$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled
        );

        private readonly HttpClient _httpClient;

        public ModuleUpdateService() : this(CreateDefaultHandler()) { }

        internal ModuleUpdateService(HttpMessageHandler handler, TimeSpan? timeout = null) {
            _httpClient = new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler))) {
                Timeout = timeout ?? TimeSpan.FromSeconds(RequestTimeoutSeconds)
            };
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Events-and-Metas-Observer-Updater");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        }

        public async Task<ModuleUpdateCheckResult> CheckAsync(string currentVersionValue, CancellationToken cancellationToken) {
            if (!ModuleReleaseVersion.TryParseManifestVersion(currentVersionValue, out ModuleReleaseVersion currentVersion)) {
                return ModuleUpdateCheckResult.Failed(ModuleUpdateFailure.InvalidCurrentVersion);
            }

            using (HttpResponseMessage response = await _httpClient.GetAsync(LatestReleaseEndpoint, cancellationToken).ConfigureAwait(false)) {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                GitHubReleaseResponse release = JsonConvert.DeserializeObject<GitHubReleaseResponse>(json);
                if (release == null || release.Draft || release.Prerelease ||
                    !ModuleReleaseVersion.TryParseReleaseTag(release.TagName, out ModuleReleaseVersion releaseVersion)) {
                    return ModuleUpdateCheckResult.Failed(ModuleUpdateFailure.InvalidRelease, currentVersion);
                }

                GitHubReleaseAsset[] matchingAssets = (release.Assets ?? Array.Empty<GitHubReleaseAsset>())
                    .Where(asset => string.Equals(asset?.Name, AssetName, StringComparison.Ordinal))
                    .ToArray();
                if (matchingAssets.Length != 1) {
                    return ModuleUpdateCheckResult.Failed(ModuleUpdateFailure.MissingAsset, currentVersion);
                }

                GitHubReleaseAsset matchingAsset = matchingAssets[0];
                Match digest = DigestPattern.Match(matchingAsset.Digest ?? string.Empty);
                if (!digest.Success) {
                    return ModuleUpdateCheckResult.Failed(ModuleUpdateFailure.InvalidDigest, currentVersion);
                }

                if (!IsTrustedAssetUrl(matchingAsset.BrowserDownloadUrl, release.TagName)) {
                    return ModuleUpdateCheckResult.Failed(ModuleUpdateFailure.InvalidAssetUrl, currentVersion);
                }

                var updateRelease = new ModuleUpdateRelease(
                    releaseVersion,
                    release.TagName,
                    matchingAsset.BrowserDownloadUrl,
                    digest.Groups["hash"].Value.ToUpperInvariant()
                );
                return ModuleUpdateCheckResult.Checked(currentVersion, updateRelease);
            }
        }

        private static HttpMessageHandler CreateDefaultHandler() {
            return new HttpClientHandler {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
        }

        private static bool IsTrustedAssetUrl(string value, string tagName) {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment)) {
                return false;
            }

            string expectedPath = $"/jakeuj/Community-Module-Pack/releases/download/{tagName}/{AssetName}";
            return string.Equals(uri.AbsolutePath, expectedPath, StringComparison.Ordinal);
        }

        public void Dispose() {
            _httpClient.Dispose();
        }

        private sealed class GitHubReleaseResponse {
            [JsonProperty("tag_name")]
            public string TagName { get; set; }

            [JsonProperty("draft")]
            public bool Draft { get; set; }

            [JsonProperty("prerelease")]
            public bool Prerelease { get; set; }

            [JsonProperty("assets")]
            public GitHubReleaseAsset[] Assets { get; set; }
        }

        private sealed class GitHubReleaseAsset {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("browser_download_url")]
            public string BrowserDownloadUrl { get; set; }

            [JsonProperty("digest")]
            public string Digest { get; set; }
        }
    }
}
