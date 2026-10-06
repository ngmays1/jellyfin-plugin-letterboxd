const PLUGIN_ID = "9114b8e2-87f8-48f1-9e7b-d7f74302f754";

function esc(value) {
    if (value === null || value === undefined) {
        return "";
    }
    return String(value)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;");
}

function pick(obj, ...names) {
    if (!obj) {
        return undefined;
    }
    for (const name of names) {
        if (obj[name] !== undefined && obj[name] !== null) {
            return obj[name];
        }
    }
    return undefined;
}

function stars(rating) {
    if (rating === null || rating === undefined) {
        return "";
    }
    const full = Math.floor(rating);
    const half = rating - full >= 0.5;
    return "★".repeat(full) + (half ? "½" : "");
}

function fmtDate(value) {
    if (!value) {
        return "";
    }
    return String(value).slice(0, 10);
}

export default function (view) {
    let pollTimer = null;
    let lastKnownSync = null;

    function apiUrl(path) {
        return window.ApiClient.getUrl(path);
    }

    function apiGet(path) {
        return window.ApiClient.getJSON(apiUrl(path));
    }

    function apiPost(path) {
        return window.ApiClient.ajax({ url: apiUrl(path), type: "POST" });
    }

    function posterHtml(rec) {
        const itemId = pick(rec, "itemId");
        if (itemId) {
            const src = esc(window.ApiClient.getUrl("Items/" + itemId + "/Images/Primary?fillWidth=100&fillHeight=150"));
            return '<img class="listItemImage" src="' + src + '" alt="" style="width:47px;border-radius:.2em;flex:0 0 auto;" />';
        }
        const posterUrl = pick(rec, "posterUrl", "posterPath");
        if (posterUrl && String(posterUrl).startsWith("http")) {
            return '<img class="listItemImage" src="' + esc(posterUrl) + '" alt="" style="width:47px;border-radius:.2em;flex:0 0 auto;" />';
        }
        return '<div style="width:47px;height:70px;background:rgba(255,255,255,.08);border-radius:.2em;flex:0 0 auto;"></div>';
    }

    function statusHtml(status) {
        const syncing = pick(status, "syncing");
        const lastSync = pick(status, "lastSyncUtc", "lastSync");
        const error = pick(status, "lastError", "error");
        const lines = [];
        if (pick(status, "configured") === false) {
            lines.push("Not configured — set your Letterboxd username above.");
        }
        lines.push("Username: " + esc(pick(status, "username") || "—"));
        lines.push("Last sync: " + (lastSync ? esc(fmtDate(lastSync)) + " " + esc(String(lastSync).slice(11, 19)) + " UTC" : "never"));
        lines.push("Diary entries: " + esc(pick(status, "diaryCount") ?? 0));
        lines.push("Suggestions in library: " + esc(pick(status, "inLibraryCount") ?? 0));
        lines.push("Missing suggestions: " + esc(pick(status, "missingCount") ?? 0));
        lines.push("Library movies indexed: " + esc(pick(status, "libraryMovieCount") ?? "—"));
        const playlistUpdated = pick(status, "playlistUpdatedUtc", "playlistUpdated");
        lines.push("Playlist: " + (playlistUpdated ? "updated " + esc(fmtDate(playlistUpdated)) : "not updated yet"));
        if (syncing) {
            lines.push("<strong>Sync running…</strong>");
        }
        if (error) {
            lines.push('<span style="color:#ff6b6b">Last error: ' + esc(error) + "</span>");
        }
        return lines.map((l) => "<div>" + l + "</div>").join("");
    }

    function renderReviews(reviews) {
        const container = view.querySelector("#LetterboxdReviews");
        if (!container) {
            return;
        }
        if (!reviews || reviews.length === 0) {
            container.innerHTML = '<div class="fieldDescription">No reviews synced yet. Run a sync first.</div>';
            return;
        }
        container.innerHTML = reviews.map((r) => {
            const inLib = pick(r, "inLibrary");
            const rating = pick(r, "rating");
            const review = pick(r, "review") || "";
            const reviewPreview = review.length > 400 ? review.slice(0, 400) + "…" : review;
            const link = pick(r, "link");
            const header = [];
            header.push("<strong>" + esc(r.title) + "</strong>");
            if (r.year) {
                header.push("(" + esc(r.year) + ")");
            }
            if (rating !== undefined) {
                header.push('<span style="letter-spacing:.15em;">' + esc(stars(rating)) + "</span>");
            }
            if (pick(r, "rewatch")) {
                header.push('<span class="secondaryText">rewatch</span>');
            }
            if (pick(r, "liked")) {
                header.push('<span class="secondaryText">liked</span>');
            }
            header.push('<span class="secondaryText">' + esc(fmtDate(pick(r, "watchedDate"))) + "</span>");
            if (inLib) {
                header.push('<span class="paperCheckbox" style="padding:.1em .5em;border-radius:.5em;background:rgba(76,175,80,.25);">in library</span>');
            }
            return (
                '<div style="display:flex;gap:1em;padding:.6em 0;border-bottom:1px solid rgba(255,255,255,.08)">' +
                '<div style="flex:1;min-width:0;">' +
                '<div>' + header.join(" ") + "</div>" +
                (reviewPreview ? '<div class="secondaryText" style="white-space:pre-wrap;margin-top:.25em;">' + esc(reviewPreview) + "</div>" : "") +
                (link ? '<div style="margin-top:.25em;"><a href="' + esc(link) + '" target="_blank" rel="noopener">View on Letterboxd</a></div>' : "") +
                "</div></div>"
            );
        }).join("");
    }

    function renderRecs(recs) {
        const container = view.querySelector("#LetterboxdRecs");
        if (!container) {
            return;
        }
        if (!recs || recs.length === 0) {
            container.innerHTML = '<div class="fieldDescription">No suggestions yet. Run a sync first.</div>';
            return;
        }
        const inLib = recs.filter((r) => pick(r, "inLibrary"));
        const missing = recs.filter((r) => !pick(r, "inLibrary"));

        function section(title, items) {
            if (items.length === 0) {
                return "";
            }
            const rows = items.map((r) => {
                const scorePct = Math.round((pick(r, "score") || 0) * 100);
                const reasons = pick(r, "reasons") || [];
                return (
                    '<div style="display:flex;gap:1em;padding:.6em 0;border-bottom:1px solid rgba(255,255,255,.08);align-items:center">' +
                    posterHtml(r) +
                    '<div style="flex:1;min-width:0;">' +
                    "<div><strong>" + esc(r.title) + "</strong>" +
                    (r.year ? " (" + esc(r.year) + ")" : "") +
                    ' <span class="secondaryText">' + scorePct + "% match</span></div>" +
                    '<div class="secondaryText">' + esc(reasons.join(" · ")) + "</div>" +
                    "</div></div>"
                );
            }).join("");
            return '<div class="verticalSection" style="margin-top:1em;"><h3 class="sectionTitle">' +
                esc(title) + " (" + items.length + ")</h3>" + rows + "</div>";
        }

        container.innerHTML =
            section("Already in your library", inLib) +
            section("Worth adding", missing);
    }

    async function loadStatus() {
        const status = await apiGet("LetterboxdPlugin/status");
        lastKnownSync = pick(status, "lastSyncUtc", "lastSync") || null;
        const el = view.querySelector("#LetterboxdStatus");
        if (el) {
            el.innerHTML = statusHtml(status);
        }
        return status;
    }

    async function loadAll() {
        if (window.Dashboard && Dashboard.showLoadingMsg) {
            Dashboard.showLoadingMsg();
        }
        try {
            const config = await window.ApiClient.getPluginConfiguration(PLUGIN_ID);
            view.querySelector("#txtLbUsername").value = pick(config, "LetterboxdUsername", "letterboxdUsername") || "";
            view.querySelector("#txtTmdbKey").value = pick(config, "TmdbApiKey", "tmdbApiKey") || "";
            view.querySelector("#numSyncHours").value = pick(config, "SyncIntervalHours", "syncIntervalHours") ?? 6;
            view.querySelector("#txtPlaylistName").value = pick(config, "PlaylistName", "playlistName") || "";
            view.querySelector("#txtPlaylistOwner").value = pick(config, "PlaylistOwnerUsername", "playlistOwnerUsername") || "";
            view.querySelector("#numMaxPlaylist").value = pick(config, "MaxPlaylistItems", "maxPlaylistItems") ?? 25;
            view.querySelector("#numMaxMissing").value = pick(config, "MaxMissingItems", "maxMissingItems") ?? 50;
            view.querySelector("#chkEnablePlaylist").checked = pick(config, "EnablePlaylistSync", "enablePlaylistSync") !== false;
            view.querySelector("#chkEnableSidebar").checked = pick(config, "EnableSidebarPage", "enableSidebarPage") !== false;

            const status = await loadStatus();
            const [reviews, recs] = await Promise.all([
                apiGet("LetterboxdPlugin/reviews"),
                apiGet("LetterboxdPlugin/recommendations")
            ]);
            renderReviews(reviews);
            renderRecs(recs);
            return status;
        } catch (err) {
            const el = view.querySelector("#LetterboxdStatus");
            if (el) {
                el.innerHTML = '<span style="color:#ff6b6b">Failed to load: ' + esc(err) + "</span>";
            }
        } finally {
            if (window.Dashboard && Dashboard.hideLoadingMsg) {
                Dashboard.hideLoadingMsg();
            }
        }
    }

    async function save(evt) {
        if (evt) {
            evt.preventDefault();
        }
        const config = await window.ApiClient.getPluginConfiguration(PLUGIN_ID);
        config.LetterboxdUsername = view.querySelector("#txtLbUsername").value.trim();
        config.TmdbApiKey = view.querySelector("#txtTmdbKey").value.trim();
        config.SyncIntervalHours = parseInt(view.querySelector("#numSyncHours").value, 10) || 6;
        config.PlaylistName = view.querySelector("#txtPlaylistName").value.trim();
        config.PlaylistOwnerUsername = view.querySelector("#txtPlaylistOwner").value.trim();
        config.MaxPlaylistItems = parseInt(view.querySelector("#numMaxPlaylist").value, 10) || 25;
        config.MaxMissingItems = parseInt(view.querySelector("#numMaxMissing").value, 10) || 50;
        config.EnablePlaylistSync = view.querySelector("#chkEnablePlaylist").checked;
        config.EnableSidebarPage = view.querySelector("#chkEnableSidebar").checked;
        await window.ApiClient.updatePluginConfiguration(PLUGIN_ID, config);
        if (window.Dashboard && Dashboard.processPluginConfigurationUpdateResult) {
            Dashboard.processPluginConfigurationUpdateResult();
        }
        await loadStatus();
    }

    function stopPolling() {
        if (pollTimer) {
            clearInterval(pollTimer);
            pollTimer = null;
        }
    }

    async function syncNow() {
        const btn = view.querySelector("#btnLbSync");
        if (btn) {
            btn.disabled = true;
            const label = btn.querySelector("span") || btn;
            label.textContent = "Syncing…";
        }
        try {
            await apiPost("LetterboxdPlugin/sync");
            let attempts = 0;
            stopPolling();
            pollTimer = setInterval(async () => {
                attempts += 1;
                try {
                    const status = await loadStatus();
                    const syncing = pick(status, "syncing");
                    const current = pick(status, "lastSyncUtc", "lastSync") || null;
                    if ((!syncing && current && current !== lastKnownSync) || attempts > 60) {
                        stopPolling();
                        lastKnownSync = current;
                        const [reviews, recs] = await Promise.all([
                            apiGet("LetterboxdPlugin/reviews"),
                            apiGet("LetterboxdPlugin/recommendations")
                        ]);
                        renderReviews(reviews);
                        renderRecs(recs);
                    }
                } catch (err) {
                    // keep polling
                }
            }, 2000);
        } catch (err) {
            const el = view.querySelector("#LetterboxdStatus");
            if (el) {
                el.innerHTML = '<span style="color:#ff6b6b">Failed to start sync: ' + esc(err) + "</span>";
            }
        } finally {
            if (btn) {
                btn.disabled = false;
                const label = btn.querySelector("span") || btn;
                label.textContent = "Sync now";
            }
        }
    }

    let wired = false;
    view.addEventListener("viewshow", () => {
        if (!wired) {
            wired = true;
            const form = view.querySelector("#LetterboxdConfigForm");
            if (form) {
                form.addEventListener("submit", save);
            }
            const syncBtn = view.querySelector("#btnLbSync");
            if (syncBtn) {
                syncBtn.addEventListener("click", syncNow);
            }
        }
        loadAll();
    });

    view.addEventListener("viewdestroy", () => {
        stopPolling();
    });
}
