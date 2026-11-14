using ImageTextify.Platform.Imaging;
using SkiaSharp;

var imagePath = @"C:\Users\HP\Desktop\exracts\Images\sharp\form_document_1.png";
var outPath = @"C:\Users\HP\Desktop\file-ferry\bench\probe\preprocessor_output.png";

using var source = SKBitmap.Decode(imagePath);
Console.WriteLine("Source: " + source.Width + "x" + source.Height);

var preprocessor = new ImagePreprocessor();
using var stream = File.OpenRead(imagePath);
var result = await preprocessor.PreprocessAsync(stream, new ImagePreprocessingOptions(), CancellationToken.None);
var enhanced = result.Enhanced.Bitmap;
Console.WriteLine("Enhanced: " + enhanced.Width + "x" + enhanced.Height);

using var data = enhanced.Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes(outPath, data.ToArray());
Console.WriteLine("Saved: " + outPath);
