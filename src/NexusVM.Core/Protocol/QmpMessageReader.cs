namespace NexusVM.Core.Protocol;

public static class QmpMessageReader
{
    /// <summary>
    /// Removes one top-level JSON object from the front of <paramref name="buffer"/>.
    /// Braces inside strings are ignored. The buffer is left unchanged when the object is incomplete.
    /// </summary>
    public static bool TryTake(ref string buffer, out string json)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var start = buffer.IndexOf('{');
        if (start < 0)
        {
            json = string.Empty;
            return false;
        }

        var depth = 0;
        var inString = false;
        var escape = false;
        for (var index = start; index < buffer.Length; index++)
        {
            var character = buffer[index];
            if (inString)
            {
                if (escape)
                    escape = false;
                else if (character == '\\')
                    escape = true;
                else if (character == '"')
                    inString = false;

                continue;
            }

            if (character == '"')
            {
                inString = true;
                continue;
            }

            if (character == '{')
            {
                depth++;
            }
            else if (character == '}')
            {
                depth--;
                if (depth == 0)
                {
                    json = buffer[start..(index + 1)];
                    buffer = buffer[(index + 1)..];
                    return true;
                }
            }
        }

        json = string.Empty;
        return false;
    }
}
