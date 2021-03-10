using System.Text;
using System.Text.Json;

namespace ActionCableSharp
{
    /// <summary>
    /// A <see cref="JsonNamingPolicy"/> that converts PascalCase/camelCase to snake_case.
    /// </summary>
    public class SnakeCaseNamingPolicy : JsonNamingPolicy
    {
        /// <summary>
        /// Converts a PascalCase/camelCase string to snake_case.
        /// </summary>
        /// <param name="name">The variable name to convert.</param>
        /// <returns>The specified variable name but in snake_case.</returns>
        public override string ConvertName(string name)
        {
            var builder = new StringBuilder();

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];

                if (i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || (i < name.Length - 1 && char.IsLower(name[i + 1]))))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLower(c));
            }

            return builder.ToString();
        }
    }
}
