# Letterboxd for Jellyfin

[![Build & Release](https://img.shields.io/github/actions/workflow/status/ngmays1/jellyfin-plugin-letterboxd/.github/workflows/release.yml?branch=master&label=release)](https://github.com/ngmays1/jellyfin-plugin-letterboxd/actions/workflows/release.yml)

Brings your [Letterboxd](https://letterboxd.com/) diary, reviews and ratings into
Jellyfin, and uses them to recommend movies from (and *for*) your library.

The plugin reads your Letterboxd diary from the **official RSS feed** (no scraping),
matches entries against your Jellyfin movies, builds a taste profile from your own
ratings, and asks [TMDB](https://www.themoviedb.org/) for similar titles. Results
surface as:

- A **playlist** of recommendations you already own (`Letterboxd Picks` by default).
- A **list of movies worth adding** (titles you don't have in the library yet).
- A **sidebar page** in the web UI showing recommendations and your recent reviews.
- A **dashboard config page** and a small REST API.

> This is an unofficial, community plugin. It is not affiliated with or endorsed by
> Letterboxd, TMDB, or the Jellyfin project.

## Requirements

- **Jellyfin 12.1** or newer (`targetAbi` 12.1.0.0). The plugin targets `net10.0`.
- A **Letterboxd account** whose diary is publicly visible (the RSS feed is read anonymously).
- A free **TMDB API key** — create one at
  <https://www.themoviedb.org/settings/api>. Reviews work without it, but
  recommendations require it.

Optional, for the sidebar page:

- [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation)
- [Plugin Pages](https://github.com/IAmParadox27/jellyfin-plugin-pages)

## Installation

1. In Jellyfin, go to **Dashboard → Plugins → Repositories → Add**.
2. Name it `Letterboxd` and use the repository URL:
   ```
   https://raw.githubusercontent.com/<owner>/<repo>/<default-branch>/manifest.json
   ```
   (replace `<owner>/<repo>/<default-branch>` with this repository's coordinates)
3. Go to **Catalog**, find **Letterboxd** and install it.
4. If you want the sidebar entry, install **File Transformation** and **Plugin
   Pages** the same way, then restart Jellyfin.

### Manual installation

Download the latest `letterboxd_<version>.zip` from the
[Releases](https://github.com/<owner>/<repo>/releases) page, extract it into
`<config>/plugins/Letterboxd/`, and restart Jellyfin.

## Configuration

Open **Dashboard → Plugins → Letterboxd**:

| Setting | Description |
| --- | --- |
| **Letterboxd username** | Your Letterboxd username (required). |
| **TMDB API key** | Your own TMDB API key (required for recommendations). |
| **Sync interval (hours)** | How often to refresh in the background. |
| **Playlist name** | Name of the generated playlist. |
| **Playlist owner** | Jellyfin user who will own the playlist (blank = first admin). |
| **Enable playlist sync** | Create/update the playlist automatically. |
| **Max playlist items** | Cap on entries written to the playlist. |
| **Max missing items** | Cap on the "worth adding" list. |
| **Enable sidebar page** | Register the Letterboxd page in the sidebar (needs Plugin Pages). |

Save, then run the **Sync Letterboxd** scheduled task (or wait for the interval).

## REST API

All endpoints are under `/LetterboxdPlugin`:

| Endpoint | Description |
| --- | --- |
| `GET /LetterboxdPlugin/status` | Last sync time, counts, configuration state. |
| `GET /LetterboxdPlugin/reviews` | Parsed diary/review entries. |
| `GET /LetterboxdPlugin/recommendations?filter=inLibrary` | Scored recommendations (`inLibrary`, `missing`, or all). |
| `POST /LetterboxdPlugin/sync` | Trigger a sync now. |
| `GET /LetterboxdPlugin/page` | HTML fragment used by the sidebar page. |

## How it works

1. Fetch and parse your Letterboxd diary RSS feed.
2. Build a weighted taste profile (genres, keywords, people) from your ratings.
3. Ask TMDB for `recommendations`/`similar` movies for each highly-rated entry.
4. Score candidates against the taste profile and your library.
5. Split results into *already in library* and *worth adding*; write the playlist,
   cache the results, and render the sidebar/dashboard surfaces.

Your API key and diary data are stored locally in the Jellyfin plugin
configuration. Nothing is sent anywhere except directly to Letterboxd and TMDB.

## Building from source

Requires the **.NET 10 SDK**. The recommended tool is
[jprm](https://github.com/oddstr13/jellyfin-plugin-repository-manager):

```sh
pip install jprm
jprm plugin build .
```

Or build the DLL directly:

```sh
dotnet publish Jellyfin.Plugin.Letterboxd/Jellyfin.Plugin.Letterboxd.csproj -c Release -o artifacts
```

## Releasing

Push a tag such as `v12.1.0.0`. The GitHub Actions workflow
(`.github/workflows/release.yml`) builds the plugin with JPRM, publishes a GitHub
Release with the packaged zip, and updates `manifest.json` on the default branch so
the repository can be added to Jellyfin's plugin catalogue.

## License

[GPL-3.0](LICENSE).

This project is unofficial and is not affiliated with or endorsed by Letterboxd,
TMDB, or the Jellyfin project.