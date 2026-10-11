using EasySearch.Core;

namespace EasySearch;

public sealed partial class MainForm
{
    CancellationTokenSource? _predictionCancel;

    void PredictFolder(string prefix)
    {
        _predictionCancel?.Cancel();
        var cancel = _predictionCancel = new CancellationTokenSource();
        Task.Run(() =>
        {
            try
            {
                var names = FolderPrediction.FindAll(_index.Index, prefix, cancel.Token);
                if (!cancel.IsCancellationRequested) Post(new { type = "folderPrediction", prefix, names });
            }
            catch (OperationCanceledException) { }
        });
    }

    void PredictDirectory(string path, string prefix)
    {
        _predictionCancel?.Cancel();
        var cancel = _predictionCancel = new CancellationTokenSource();
        Task.Run(() =>
        {
            try
            {
                var names = DirectoryPrediction.FindAll(path, prefix, cancel.Token);
                if (!cancel.IsCancellationRequested) Post(new { type = "directoryPrediction", path, prefix, names });
            }
            catch (OperationCanceledException) { }
        });
    }
}
