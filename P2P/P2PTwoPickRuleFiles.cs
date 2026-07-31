using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace Shadowbus
{
    internal static class P2PTwoPickRuleFiles
    {
        internal static IReadOnlyList<P2PTwoPickRuleDefinition> Load(
            string directory,
            JsonSerializerSettings settings,
            Func<P2PTwoPickRuleDefinition, string, P2PTwoPickRuleDefinition> normalize,
            Action<string> reportError = null)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A Two Pick rule directory is required.", nameof(directory));
            }
            if (normalize == null)
            {
                throw new ArgumentNullException(nameof(normalize));
            }

            List<P2PTwoPickRuleDefinition> definitions =
                new List<P2PTwoPickRuleDefinition>();
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(directory))
            {
                return definitions.AsReadOnly();
            }

            string[] paths = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                try
                {
                    P2PTwoPickRuleDefinition source =
                        JsonConvert.DeserializeObject<P2PTwoPickRuleDefinition>(
                            File.ReadAllText(path),
                            settings);
                    if (source == null)
                    {
                        throw new FormatException("The rule file is empty.");
                    }

                    string fileId = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrWhiteSpace(source.Id))
                    {
                        source.Id = fileId;
                    }
                    P2PTwoPickRuleDefinition definition = normalize(source, fileId);
                    if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                    {
                        throw new FormatException("The rule ID is empty.");
                    }
                    if (!ids.Add(definition.Id))
                    {
                        reportError?.Invoke(
                            $"Duplicate Two Pick rule ID '{definition.Id}' in '{path}'; " +
                            "the later file was ignored.");
                        continue;
                    }
                    definitions.Add(definition);
                }
                catch (Exception ex)
                {
                    reportError?.Invoke(
                        $"Failed to load Two Pick rule '{path}': {ex.Message}");
                }
            }
            return definitions.AsReadOnly();
        }
    }
}
