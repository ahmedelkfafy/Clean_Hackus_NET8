using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clean_Hackus_NET8.Models;

namespace Clean_Hackus_NET8.Services;

/// <summary>
/// Parses email:password combo files into a thread-safe queue.
/// Supported formats: email:password, email;password
/// </summary>
public class ComboLoader
{
    /// <summary>
    /// Load a combo file and return a thread-safe queue of Mailbox objects.
    /// </summary>
    public static ConcurrentQueue<Mailbox> LoadFromFile(string filePath)
    {
        var queue = new ConcurrentQueue<Mailbox>();
        var lines = File.ReadAllLines(filePath);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Support both : and ; delimiters
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0) separatorIndex = line.IndexOf(';');
            if (separatorIndex <= 0) continue;

            var email = line[..separatorIndex].Trim();
            var password = line[(separatorIndex + 1)..].Trim();

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password)) continue;
            if (!email.Contains('@')) continue;

            var atIndex = email.IndexOf('@');
            var domain = email[(atIndex + 1)..].ToLowerInvariant();

            queue.Enqueue(new Mailbox(email, password, domain));
        }

        return queue;
    }

    /// <summary>
    /// Count lines in a combo file without fully loading them.
    /// </summary>
    public static int CountLines(string filePath)
    {
        return File.ReadLines(filePath).Count(l => !string.IsNullOrWhiteSpace(l) && l.Contains('@'));
    }
}
