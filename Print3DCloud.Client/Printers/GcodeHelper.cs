using System.Text;

namespace Print3DCloud.Client.Printers
{
    public static class GcodeHelper
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string SanitizeGcodeCommand(string text)
        {
            StringBuilder builder = new(0, text.Length);

            bool isInComment = false;

            foreach (char c in text)
            {
                switch (c)
                {
                    // does this need to be multi-line??
                    case '(':
                        isInComment = true;
                        break;

                    case ')' when isInComment:
                        isInComment = false;
                        break;

                    case '\n':
                    case ';':
                        return builder.ToString();

                    default:
                        if (isInComment) break;

                        builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Get the G-code command code (starts with G or M).
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string GetGcodeCommandCode(string text)
        {
            StringBuilder builder = new(4);

            foreach (char c in text)
            {
                if (c is ' ' or not 'G' and not 'M' && !char.IsLetterOrDigit(c))
                {
                    break;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }
    }
}