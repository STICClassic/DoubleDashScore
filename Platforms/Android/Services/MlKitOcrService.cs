using Android.Gms.Tasks;
using Android.Graphics;
using DoubleDashScore.Models;
using DoubleDashScore.Services;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;
using CancellationToken = System.Threading.CancellationToken;
using MlKitText = Xamarin.Google.MLKit.Vision.Text.Text;
using Task = System.Threading.Tasks.Task;

namespace DoubleDashScore.Platforms.Android.Services;

public sealed class MlKitOcrService : IOcrService
{
    public async Task<OcrResult> RecognizeAsync(Stream image, CancellationToken ct = default)
    {
        using var bitmap = await Task.Run(() => BitmapFactory.DecodeStream(image), ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Bilden kunde inte avkodas.");

        var inputImage = InputImage.FromBitmap(bitmap, 0);
        var recognizer = TextRecognition.GetClient(TextRecognizerOptions.DefaultOptions);
        try
        {
            var tcs = new TaskCompletionSource<MlKitText>();
            var task = recognizer.Process(inputImage);
            task.AddOnSuccessListener(new SuccessListener(tcs));
            task.AddOnFailureListener(new FailureListener(tcs));
            using var registration = ct.Register(() => tcs.TrySetCanceled(ct));

            var mlKitResult = await tcs.Task.ConfigureAwait(false);
            return BuildOcrResult(mlKitResult, bitmap.Width, bitmap.Height);
        }
        finally
        {
            recognizer.Close();
        }
    }

    private static OcrResult BuildOcrResult(MlKitText text, int width, int height)
    {
        var tokens = new List<OcrToken>();
        foreach (var block in text.TextBlocks)
        {
            foreach (var line in block.Lines)
            {
                foreach (var element in line.Elements)
                {
                    var box = element.BoundingBox;
                    if (box is null) continue;
                    tokens.Add(new OcrToken(
                        element.Text ?? string.Empty,
                        new OcrBoundingBox(box.Left, box.Top, box.Right, box.Bottom)));
                }
            }
        }
        return new OcrResult(tokens, width, height);
    }

    private sealed class SuccessListener : Java.Lang.Object, IOnSuccessListener
    {
        private readonly TaskCompletionSource<MlKitText> _tcs;
        public SuccessListener(TaskCompletionSource<MlKitText> tcs) => _tcs = tcs;
        public void OnSuccess(Java.Lang.Object? result)
        {
            if (result is MlKitText text) _tcs.TrySetResult(text);
            else _tcs.TrySetException(new InvalidOperationException("ML Kit returnerade oväntad typ."));
        }
    }

    private sealed class FailureListener : Java.Lang.Object, IOnFailureListener
    {
        private readonly TaskCompletionSource<MlKitText> _tcs;
        public FailureListener(TaskCompletionSource<MlKitText> tcs) => _tcs = tcs;
        public void OnFailure(Java.Lang.Exception ex) =>
            _tcs.TrySetException(new InvalidOperationException(ex.Message ?? "OCR-fel.", ex));
    }
}
