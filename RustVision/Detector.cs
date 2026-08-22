using System;
using System.Collections.Generic;
using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Dnn;

public class BoundingBox
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Cx { get; set; }
    public int Cy { get; set; }
}

public class Detector
{
    private Net? _net;

    public void LoadModel(string protoPath, string caffePath)
    {
        _net = CvDnn.ReadNetFromCaffe(protoPath, caffePath);
        _net.SetPreferableBackend(Backend.OPENCV);
        _net.SetPreferableTarget(Target.CPU);
        Cv2.SetNumThreads(Environment.ProcessorCount); // Разблокировка всех ядер i7
    }

    public List<BoundingBox> Detect(Bitmap roiFrame, int roiAbsoluteX, int roiAbsoluteY, int resolution, int maxDistance)
    {
        List<BoundingBox> results = new List<BoundingBox>();
        if (_net == null) return results;

        using (Mat matFrame = OpenCvSharp.Extensions.BitmapConverter.ToMat(roiFrame))
        using (Mat matRgb = new Mat())
        {
            Cv2.CvtColor(matFrame, matRgb, ColorConversionCodes.BGRA2RGB);

            // Премиум-опция точности 300х300 или кастомного разрешения
            using (Mat blob = CvDnn.BlobFromImage(matRgb, 0.007843, new OpenCvSharp.Size(resolution, resolution), new Scalar(127.5, 127.5, 127.5), false, false))
            {
                _net.SetInput(blob);
                using (Mat detections = _net.Forward())
                {
                    int rows = detections.Size(2);
                    for (int i = 0; i < rows; i++)
                    {
                        float confidence = detections.At<float>(0, 0, i, 2);

                        if (confidence > 0.35f)
                        {
                            int classId = (int)detections.At<float>(0, 0, i, 1);
                            if (classId == 1 || classId == 15) // Игроки Rust
                            {
                                float xmin = detections.At<float>(0, 0, i, 3) * roiFrame.Width;
                                float ymin = detections.At<float>(0, 0, i, 4) * roiFrame.Height;
                                float xmax = detections.At<float>(0, 0, i, 5) * roiFrame.Width;
                                float ymax = detections.At<float>(0, 0, i, 6) * roiFrame.Height;

                                int w = (int)(xmax - xmin);
                                int h = (int)(ymax - ymin);

                                // Умный фильтр дальности на основе геометрии рамок
                                if (w > 5 && h > 5 && (w + h) > (320 - maxDistance))
                                {
                                    results.Add(new BoundingBox
                                    {
                                        X = (int)xmin + roiAbsoluteX,
                                        Y = (int)ymin + roiAbsoluteY,
                                        Width = w,
                                        Height = h,
                                        Cx = (int)xmin + roiAbsoluteX + (w / 2),
                                        Cy = (int)ymin + roiAbsoluteY + (h / 2)
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }
        return results;
    }
}
