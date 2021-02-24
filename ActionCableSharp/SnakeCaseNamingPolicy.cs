using System.Text;
using System.Text.Json;

namespace ActionCableSharp
{
    /// <summary>
    /// <see cref="JsonNamingPolicy"/> that converts PascalCase to snake_case.
    /// </summary>
    public class SnakeCaseNamingPolicy : JsonNamingPolicy
    {
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
