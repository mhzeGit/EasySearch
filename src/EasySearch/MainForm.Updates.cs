using EasyTools.Updating;

namespace EasySearch;

public sealed partial class MainForm
{
    readonly AppUpdater _updater = new("mhzeGit/EasySearch", "EasySearch", "EasySearch.exe");
    UpdateRelease? _availableUpdate;
    bool _updateBusy;
    string _updateStatus = AppUpdater.LastFailure() ?? "Check GitHub for the latest build. Settings and index are preserved.";

    void PostUpdateState() => Post(new { type = "updateState", busy = _updateBusy, available = _availableUpdate is not null, text = _updateStatus });

    async Task RunUpdateAsync(bool install)
    {
        if (_updateBusy) return;
        _updateBusy = true;
        _updateStatus = install ? "Preparing update…" : "Checking GitHub…";
        PostUpdateState();
        try
        {
            if (install && _availableUpdate is { } release)
            {
                var progress = new Progress<string>(text => { _updateStatus = text; PostUpdateState(); });
                await _updater.InstallAsync(release, progress);
                Quit();
                return;
            }
            _availableUpdate = await _updater.CheckAsync();
            _updateStatus = _availableUpdate is { } found
                ? $"A new build ({found.Version}) is available. Update will restart EasySearch in the background."
                : "You're up to date.";
        }
        catch (Exception error)
        {
            _availableUpdate = null;
            _updateStatus = "Update failed: " + error.Message;
        }
        finally { _updateBusy = false; PostUpdateState(); }
    }
}
