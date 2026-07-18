using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using IndustrialVisionHost.Models;
using OpenCvSharp;

namespace IndustrialVisionHost.Services
{
    public sealed class NgImageStorageService
    {
        private readonly object fileSync = new object();

        public NgImageStorageService(
            string? imageDirectory = null,
            int retentionDays = 30,
            long maximumStorageBytes = 1024L * 1024 * 1024)
        {
            if (retentionDays <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(retentionDays),
                    "图片保留天数必须大于 0。");
            }

            if (maximumStorageBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumStorageBytes),
                    "图片存储容量上限必须大于 0。");
            }

            ImageDirectory = imageDirectory ?? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "IndustrialVisionHost",
                "NGImages");
            RetentionDays = retentionDays;
            MaximumStorageBytes = maximumStorageBytes;
        }

        public string ImageDirectory { get; }

        public int RetentionDays { get; }

        public long MaximumStorageBytes { get; }

        public long MaximumStorageMegabytes =>
            MaximumStorageBytes / (1024 * 1024);

        public bool TrySave(
            Mat annotatedImage,
            InspectionResult result,
            out string? savedPath,
            out string? errorMessage)
        {
            if (annotatedImage is null)
            {
                throw new ArgumentNullException(nameof(annotatedImage));
            }

            if (result is null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            savedPath = null;

            if (annotatedImage.Empty())
            {
                errorMessage = "待保存的 NG 标注图为空。";
                return false;
            }

            try
            {
                DateTime now = DateTime.Now;
                string dateDirectory = Path.Combine(
                    ImageDirectory,
                    now.ToString("yyyy-MM-dd"));
                string fileName =
                    $"{now:yyyyMMdd_HHmmss_fff}_" +
                    $"{result.JudgementCode}_" +
                    $"{Guid.NewGuid():N}.jpg";
                string filePath = Path.Combine(dateDirectory, fileName);

                lock (fileSync)
                {
                    Directory.CreateDirectory(dateDirectory);
                    bool saved = Cv2.ImWrite(
                        filePath,
                        annotatedImage,
                        new[]
                        {
                            new ImageEncodingParam(
                                ImwriteFlags.JpegQuality,
                                90)
                        });

                    if (!saved)
                    {
                        errorMessage = "OpenCV 未能写入 NG 图片。";
                        return false;
                    }
                }

                savedPath = filePath;
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TryCleanup(
            out int deletedFileCount,
            out string? errorMessage)
        {
            deletedFileCount = 0;

            if (!Directory.Exists(ImageDirectory))
            {
                errorMessage = null;
                return true;
            }

            try
            {
                lock (fileSync)
                {
                    DateTime cutoffUtc = DateTime.UtcNow.AddDays(-RetentionDays);
                    List<FileInfo> files = GetImageFiles();

                    foreach (FileInfo file in files)
                    {
                        if (file.LastWriteTimeUtc < cutoffUtc)
                        {
                            file.Delete();
                            deletedFileCount++;
                        }
                    }

                    files = GetImageFiles()
                        .OrderBy(file => file.LastWriteTimeUtc)
                        .ToList();
                    long totalBytes = files.Sum(file => file.Length);

                    foreach (FileInfo file in files)
                    {
                        if (totalBytes <= MaximumStorageBytes)
                        {
                            break;
                        }

                        long fileLength = file.Length;
                        file.Delete();
                        totalBytes -= fileLength;
                        deletedFileCount++;
                    }
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private List<FileInfo> GetImageFiles()
        {
            return new DirectoryInfo(ImageDirectory)
                .EnumerateFiles("*.jpg", SearchOption.AllDirectories)
                .ToList();
        }
    }
}
