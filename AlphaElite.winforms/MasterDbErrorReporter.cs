using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using AlphaElite.infrastructure.data;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Turns a master-database failure into (a) a short, actionable message for the operator and
    /// (b) a full diagnostic record on disk.
    ///
    /// The Super Admin screen used to print the raw exception message, which for a dropped cloud
    /// connection is EF Core's own advice:
    ///     "An exception has been raised that is likely due to a transient failure. Consider
    ///      enabling transient error resiliency by adding 'EnableRetryOnFailure' ..."
    /// That is a message for a developer, not for the person running the app.
    ///
    /// The classification is deliberately honest (see
    /// <see cref="MasterDbContextOptions.IsAvailabilityFailure"/>): only reachability/connection
    /// failures are reported as "temporarily unavailable". An authentication, configuration or
    /// schema problem is shown as itself, so nobody is told to "try again" for a problem that
    /// retrying cannot fix. The original exception - with its inner exceptions - is ALWAYS logged
    /// before the dialog is shown and is never swallowed.
    /// </summary>
    internal static class MasterDbErrorReporter
    {
        /// <summary>Exactly the message the operator sees for a genuinely unreachable master database.</summary>
        internal const string MasterDbUnavailableText =
            "The Master Database is temporarily unavailable.\r\n" +
            "Please check the database connection and try again.";

        /// <summary>Diagnostics file written next to the executable (best effort; never throws).</summary>
        internal static string DiagnosticsLogPath =>
            Path.Combine(AppContext.BaseDirectory, "master-db-diagnostics.log");

        /// <summary>
        /// Builds the operator-facing text. Availability failures get the friendly message;
        /// permanent problems keep their real (single-line) reason so a wrong password or a broken
        /// configuration is never disguised as a temporary outage.
        /// </summary>
        internal static string BuildUserMessage(string headline, Exception exception)
        {
            var reason = MasterDbContextOptions.IsAvailabilityFailure(exception)
                ? MasterDbUnavailableText
                : exception.GetBaseException().Message;

            return headline + "\r\n\r\n" + reason +
                   "\r\n\r\n(Technical details were logged to " + DiagnosticsLogPath + ".)";
        }

        /// <summary>Logs the original exception (and its inner chain) and shows the friendly dialog.</summary>
        internal static void ShowDialog(IWin32Window? owner, string headline, Exception exception)
        {
            var message = BuildUserMessage(headline, exception);
            Log(headline, exception);

            if (owner != null)
            {
                MessageBox.Show(owner, message, "Master Administration", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(message, "Master Administration", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Appends the full failure to the diagnostics log. Best effort by design: a failure to
        /// write a log file must never replace or hide the database error being reported.
        /// </summary>
        internal static void Log(string headline, Exception exception)
        {
            try
            {
                var text = new StringBuilder()
                    .Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ")
                    .AppendLine(headline)
                    .AppendLine("Inner chain:");

                var depth = 0;
                for (var current = exception; current != null; current = current.InnerException, depth++)
                {
                    text.Append("  ").Append(new string('>', depth)).Append(' ')
                        .Append(current.GetType().FullName).Append(": ").AppendLine(current.Message);
                }

                text.AppendLine("Exception detail:")
                    .AppendLine(exception.ToString())
                    .AppendLine(new string('-', 80));

                File.AppendAllText(DiagnosticsLogPath, text.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Diagnostics only - never let logging change what the user is told.
            }
        }
    }
}
