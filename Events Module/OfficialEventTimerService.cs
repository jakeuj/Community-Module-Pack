using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Newtonsoft.Json;

namespace Events_Module {

    internal enum OfficialEventTimerSource {
        OfficialWiki,
        LastKnownGoodCache
    }

    internal sealed class OfficialEventTimerSourceResult {
        public OfficialEventTimerSource Source { get; set; }
        public long RevisionId { get; set; }
        public DateTime RevisionTimestampUtc { get; set; }
        public DateTime LastCheckedUtc { get; set; }
        public string Sha1 { get; set; }
        public string WidgetVersion { get; set; }
        public IReadOnlyList<OfficialEventDefinition> Events { get; set; }
        public string Error { get; set; }
        public bool TimedOut { get; set; }
    }

    internal sealed class OfficialEventTimerCache {
        public long RevisionId { get; set; }
        public DateTime RevisionTimestampUtc { get; set; }
        public DateTime LastCheckedUtc { get; set; }
        public string Sha1 { get; set; }
        public string WidgetVersion { get; set; }
        public string Content { get; set; }
    }

    internal sealed class MediaWikiApiResponse {
        [JsonProperty("query")]
        public MediaWikiQuery Query { get; set; }

        [JsonProperty("error")]
        public MediaWikiApiError Error { get; set; }
    }

    internal sealed class MediaWikiApiError {
        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("info")]
        public string Info { get; set; }
    }

    internal sealed class MediaWikiQuery {
        [JsonProperty("pages")]
        public List<MediaWikiPage> Pages { get; set; }
    }

    internal sealed class MediaWikiPage {
        [JsonProperty("revisions")]
        public List<MediaWikiRevision> Revisions { get; set; }
    }

    internal sealed class MediaWikiRevision {
        [JsonProperty("revid")]
        public long RevisionId { get; set; }

        [JsonProperty("timestamp")]
        public DateTime RevisionTimestampUtc { get; set; }

        [JsonProperty("sha1")]
        public string Sha1 { get; set; }

        [JsonProperty("slots")]
        public MediaWikiSlots Slots { get; set; }
    }

    internal sealed class MediaWikiSlots {
        [JsonProperty("main")]
        public MediaWikiSlot Main { get; set; }
    }

    internal sealed class MediaWikiSlot {
        [JsonProperty("content")]
        public string Content { get; set; }
    }

    internal sealed class OfficialEventTimerService : IDisposable {

        private static readonly Logger Logger = Logger.GetLogger<OfficialEventTimerService>();
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);
        internal const int RequestTimeoutSeconds = OfficialEventTimerEndpoint.RequestTimeoutSeconds;
        // ArenaNet's GW2 API exposes world-boss identifiers, but not the complete timer schedule or waypoints.
        // The English GW2 Wiki's MediaWiki API is therefore the authoritative machine-readable source used here.

        private readonly string _cachePath;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

        public OfficialEventTimerService(string cacheDirectory) {
            if (string.IsNullOrWhiteSpace(cacheDirectory)) throw new ArgumentNullException(nameof(cacheDirectory));

            Directory.CreateDirectory(cacheDirectory);
            _cachePath = Path.Combine(cacheDirectory, "official-event-timer.json");

            var handler = new HttpClientHandler {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler) {
                Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds)
            };
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", OfficialEventTimerEndpoint.UserAgent);
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        }

        public async Task<OfficialEventTimerSourceResult> RefreshAsync(bool force, CancellationToken cancellationToken) {
            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try {
                OfficialEventTimerCache cache = ReadValidCache();

                DateTime utcNow = DateTime.UtcNow;
                if (!force && cache != null && cache.LastCheckedUtc <= utcNow.AddMinutes(5) &&
                    utcNow - cache.LastCheckedUtc < RefreshInterval) {
                    return ToResult(cache, OfficialEventTimerSource.LastKnownGoodCache, null);
                }

                try {
                    if (cache == null) {
                        MediaWikiRevision initialRevision = await FetchRevisionAsync(
                            includeContent: true,
                            bypassCache: true,
                            cancellationToken
                        ).ConfigureAwait(false);
                        return AcceptRevision(initialRevision);
                    }

                    MediaWikiRevision metadata = await FetchRevisionAsync(includeContent: false, bypassCache: force, cancellationToken).ConfigureAwait(false);

                    if (cache.RevisionId == metadata.RevisionId &&
                        string.Equals(cache.Sha1, metadata.Sha1, StringComparison.OrdinalIgnoreCase)) {
                        cache.LastCheckedUtc = DateTime.UtcNow;
                        TrySaveCache(cache);
                        return ToResult(cache, OfficialEventTimerSource.OfficialWiki, null);
                    }

                    MediaWikiRevision revision = await FetchRevisionAsync(includeContent: true, bypassCache: true, cancellationToken).ConfigureAwait(false);
                    return AcceptRevision(revision);
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                } catch (TaskCanceledException exception) {
                    Logger.Warn(exception, $"The official Guild Wars 2 Wiki event timer request exceeded {RequestTimeoutSeconds} seconds.");
                    return cache == null
                        ? new OfficialEventTimerSourceResult { Error = exception.Message, TimedOut = true }
                        : ToResult(cache, OfficialEventTimerSource.LastKnownGoodCache, exception.Message);
                } catch (Exception exception) {
                    Logger.Warn(exception, "Failed to refresh the official Guild Wars 2 Wiki event timer data.");
                    return cache == null
                        ? new OfficialEventTimerSourceResult { Error = exception.Message }
                        : ToResult(cache, OfficialEventTimerSource.LastKnownGoodCache, exception.Message);
                }
            } finally {
                _refreshLock.Release();
            }
        }

        private async Task<MediaWikiRevision> FetchRevisionAsync(bool includeContent,
                                                                  bool bypassCache,
                                                                  CancellationToken cancellationToken) {
            string revisionProperties = includeContent
                ? "ids%7Ctimestamp%7Csha1%7Ccontent"
                : "ids%7Ctimestamp%7Csha1";
            int cacheAge = bypassCache ? 0 : (int)RefreshInterval.TotalSeconds;
            string slots = includeContent ? "&rvslots=main" : string.Empty;
            string uri = OfficialEventTimerEndpoint.ApiEndpoint +
                         "?action=query&prop=revisions&titles=Widget%3AEvent_timer%2Fdata.json" +
                         "&rvprop=" + revisionProperties + slots +
                         "&format=json&formatversion=2&maxlag=5" +
                         "&maxage=" + cacheAge.ToString() + "&smaxage=" + cacheAge.ToString();

            using (HttpResponseMessage response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false)) {
                response.EnsureSuccessStatusCode();
                string responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                MediaWikiApiResponse apiResponse = JsonConvert.DeserializeObject<MediaWikiApiResponse>(responseJson);

                if (apiResponse?.Error != null) {
                    throw new HttpRequestException($"MediaWiki API error {apiResponse.Error.Code}: {apiResponse.Error.Info}");
                }

                MediaWikiRevision revision = apiResponse?.Query?.Pages?[0]?.Revisions?[0];
                if (revision == null || revision.RevisionId <= 0 || string.IsNullOrWhiteSpace(revision.Sha1)) {
                    throw new InvalidDataException("The MediaWiki API returned no valid event timer revision.");
                }

                return revision;
            }
        }

        private OfficialEventTimerSourceResult AcceptRevision(MediaWikiRevision revision) {
            if (string.IsNullOrWhiteSpace(revision.Slots?.Main?.Content)) {
                throw new InvalidDataException("The official Wiki revision has no content.");
            }

            OfficialEventTimerParseResult parsed = OfficialEventTimerParser.Parse(revision.Slots.Main.Content);
            var updatedCache = new OfficialEventTimerCache {
                RevisionId = revision.RevisionId,
                RevisionTimestampUtc = revision.RevisionTimestampUtc.ToUniversalTime(),
                LastCheckedUtc = DateTime.UtcNow,
                Sha1 = revision.Sha1,
                WidgetVersion = parsed.Version,
                Content = revision.Slots.Main.Content
            };

            TrySaveCache(updatedCache);
            return ToResult(updatedCache, OfficialEventTimerSource.OfficialWiki, null, parsed);
        }

        private OfficialEventTimerCache ReadValidCache() {
            if (!File.Exists(_cachePath)) return null;

            try {
                OfficialEventTimerCache cache = JsonConvert.DeserializeObject<OfficialEventTimerCache>(File.ReadAllText(_cachePath));
                if (cache == null || cache.RevisionId <= 0 || string.IsNullOrWhiteSpace(cache.Sha1) || string.IsNullOrWhiteSpace(cache.Content)) {
                    return null;
                }

                OfficialEventTimerParseResult parsed = OfficialEventTimerParser.Parse(cache.Content);
                cache.WidgetVersion = parsed.Version;
                return cache;
            } catch (Exception exception) {
                Logger.Warn(exception, "Ignored an invalid cached Guild Wars 2 Wiki event timer payload.");
                return null;
            }
        }

        private OfficialEventTimerSourceResult ToResult(OfficialEventTimerCache cache,
                                                        OfficialEventTimerSource source,
                                                        string error,
                                                        OfficialEventTimerParseResult parsed = null) {
            parsed = parsed ?? OfficialEventTimerParser.Parse(cache.Content);
            return new OfficialEventTimerSourceResult {
                Source = source,
                RevisionId = cache.RevisionId,
                RevisionTimestampUtc = cache.RevisionTimestampUtc,
                LastCheckedUtc = cache.LastCheckedUtc,
                Sha1 = cache.Sha1,
                WidgetVersion = parsed.Version,
                Events = parsed.Events,
                Error = error
            };
        }

        private void SaveCache(OfficialEventTimerCache cache) {
            string temporaryPath = _cachePath + ".tmp";
            string json = JsonConvert.SerializeObject(cache, Formatting.Indented);
            File.WriteAllText(temporaryPath, json);

            if (!File.Exists(_cachePath)) {
                File.Move(temporaryPath, _cachePath);
                return;
            }

            try {
                File.Replace(temporaryPath, _cachePath, null);
            } catch (PlatformNotSupportedException) {
                File.Copy(temporaryPath, _cachePath, true);
                File.Delete(temporaryPath);
            } catch (IOException) {
                File.Copy(temporaryPath, _cachePath, true);
                File.Delete(temporaryPath);
            }
        }

        private void TrySaveCache(OfficialEventTimerCache cache) {
            try {
                SaveCache(cache);
            } catch (Exception exception) {
                Logger.Warn(exception, "Could not persist the validated Guild Wars 2 Wiki event timer cache.");
            }
        }

        public void Dispose() {
            _httpClient.Dispose();
            _refreshLock.Dispose();
        }
    }
}
