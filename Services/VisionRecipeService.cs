using System;
using System.IO;
using System.Text;
using System.Text.Json;

using IndustrialVisionHost.Models;

namespace IndustrialVisionHost.Services
{
    public sealed class VisionRecipeService
    {
        private const int CurrentFileVersion = 2;
        private readonly object fileSync = new object();
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public VisionRecipeService(string? recipeDirectory = null)
        {
            RecipeDirectory = recipeDirectory ?? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "IndustrialVisionHost",
                "Recipes");
            RecipePath = Path.Combine(RecipeDirectory, "LastRecipe.json");
        }

        public string RecipeDirectory { get; }

        public string RecipePath { get; }

        public bool TrySave(
            string recipeName,
            VisionParameters parameters,
            out int savedRecipeRevision,
            out string? errorMessage)
        {
            if (parameters is null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            savedRecipeRevision = 0;
            string temporaryPath = RecipePath + ".tmp";

            try
            {
                string normalizedRecipeName = NormalizeRecipeName(recipeName);
                lock (fileSync)
                {
                    int nextRevision = GetNextRecipeRevision(
                        normalizedRecipeName);
                    var recipeFile = new RecipeFile
                    {
                        Version = CurrentFileVersion,
                        RecipeName = normalizedRecipeName,
                        RecipeRevision = nextRevision,
                        SavedAtUtc = DateTime.UtcNow,
                        Parameters = parameters
                    };

                    string json = JsonSerializer.Serialize(
                        recipeFile,
                        jsonOptions);
                    Directory.CreateDirectory(RecipeDirectory);
                    File.WriteAllText(
                        temporaryPath,
                        json,
                        new UTF8Encoding(false));
                    File.Move(temporaryPath, RecipePath, true);
                    savedRecipeRevision = nextRevision;
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                TryDeleteTemporaryFile(temporaryPath);
                savedRecipeRevision = 0;
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TryLoad(
            out VisionParameters? parameters,
            out string recipeName,
            out int recipeRevision,
            out string? errorMessage)
        {
            parameters = null;
            recipeName = string.Empty;
            recipeRevision = 0;

            if (!File.Exists(RecipePath))
            {
                errorMessage = null;
                return false;
            }

            try
            {
                RecipeFile? recipeFile;

                lock (fileSync)
                {
                    string json = File.ReadAllText(RecipePath, Encoding.UTF8);
                    recipeFile = JsonSerializer.Deserialize<RecipeFile>(
                        json,
                        jsonOptions);
                }

                if (recipeFile is null || recipeFile.Parameters is null)
                {
                    errorMessage = "配方文件内容为空或缺少视觉参数。";
                    return false;
                }

                if (recipeFile.Version < 1 ||
                    recipeFile.Version > CurrentFileVersion)
                {
                    errorMessage =
                        $"不支持的配方版本：{recipeFile.Version}。";
                    return false;
                }

                parameters = recipeFile.Parameters;
                recipeName = recipeFile.Version == 1 ||
                    string.IsNullOrWhiteSpace(recipeFile.RecipeName)
                    ? "默认配方"
                    : recipeFile.RecipeName.Trim();
                recipeRevision = recipeFile.Version == 1 ||
                    recipeFile.RecipeRevision < 1
                    ? 1
                    : recipeFile.RecipeRevision;
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private int GetNextRecipeRevision(string recipeName)
        {
            if (!File.Exists(RecipePath))
            {
                return 1;
            }

            string json = File.ReadAllText(RecipePath, Encoding.UTF8);
            RecipeFile? existing = JsonSerializer.Deserialize<RecipeFile>(
                json,
                jsonOptions);
            if (existing is null)
            {
                throw new InvalidDataException("现有配方文件内容为空。");
            }

            string existingName = existing.Version == 1 ||
                string.IsNullOrWhiteSpace(existing.RecipeName)
                ? "默认配方"
                : existing.RecipeName.Trim();
            int existingRevision = existing.Version == 1 ||
                existing.RecipeRevision < 1
                ? 1
                : existing.RecipeRevision;

            return string.Equals(
                    existingName,
                    recipeName,
                    StringComparison.OrdinalIgnoreCase)
                ? checked(existingRevision + 1)
                : 1;
        }

        private static string NormalizeRecipeName(string recipeName)
        {
            string normalized = (recipeName ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                throw new ArgumentException("配方名称不能为空。", nameof(recipeName));
            }

            if (normalized.Length > 50)
            {
                throw new ArgumentException(
                    "配方名称最多允许 50 个字符。",
                    nameof(recipeName));
            }

            foreach (char character in normalized)
            {
                if (char.IsControl(character))
                {
                    throw new ArgumentException(
                        "配方名称不能包含换行等控制字符。",
                        nameof(recipeName));
                }
            }

            return normalized;
        }

        private static void TryDeleteTemporaryFile(string temporaryPath)
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // 清理失败不覆盖真正的保存错误。
            }
        }

        private sealed class RecipeFile
        {
            public int Version { get; set; }

            public string RecipeName { get; set; } = string.Empty;

            public int RecipeRevision { get; set; }

            public DateTime SavedAtUtc { get; set; }

            public VisionParameters? Parameters { get; set; }
        }
    }
}
