using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Letterboxd.Playlists;

public sealed class PlaylistSyncService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<PlaylistSyncService> _logger;

    public PlaylistSyncService(
        ILibraryManager libraryManager,
        IPlaylistManager playlistManager,
        IUserManager userManager,
        ILogger<PlaylistSyncService> logger)
    {
        _libraryManager = libraryManager;
        _playlistManager = playlistManager;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Guid?> SyncAsync(
        string playlistName,
        IReadOnlyList<Guid> itemIds,
        string? ownerUsername,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = ResolveOwner(ownerUsername);
        if (owner is null)
        {
            _logger.LogWarning("Cannot sync playlist {Name}: no owner user could be resolved", playlistName);
            return null;
        }

        var existing = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Playlist],
            Recursive = true
        }).FirstOrDefault(p => string.Equals(p.Name, playlistName, StringComparison.OrdinalIgnoreCase));

        Guid playlistId;
        if (existing is null)
        {
            var result = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = playlistName,
                UserId = owner.Id,
                MediaType = MediaType.Video,
                Public = true,
                ItemIdList = itemIds.ToList()
            }).ConfigureAwait(false);

            if (!Guid.TryParse(result.Id, out playlistId))
            {
                _logger.LogError("Playlist creation returned unexpected id '{Id}'", result.Id);
                return null;
            }

            _logger.LogInformation("Created playlist {Name} ({Count} items)", playlistName, itemIds.Count);
        }
        else
        {
            playlistId = existing.Id;
            await _playlistManager.UpdatePlaylist(new PlaylistUpdateRequest
            {
                Id = playlistId,
                UserId = owner.Id,
                Ids = itemIds.ToList(),
                Public = true
            }).ConfigureAwait(false);

            _logger.LogInformation("Updated playlist {Name} ({Count} items)", playlistName, itemIds.Count);
        }

        return playlistId;
    }

    private User? ResolveOwner(string? ownerUsername)
    {
        if (!string.IsNullOrWhiteSpace(ownerUsername))
        {
            var match = _userManager.GetUserByName(ownerUsername);
            if (match is not null)
            {
                return match;
            }

            _logger.LogWarning("Playlist owner '{Name}' not found, falling back to first user", ownerUsername);
        }

        return _userManager.GetFirstUser();
    }
}
