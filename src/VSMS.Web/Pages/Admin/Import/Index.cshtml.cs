using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VSMS.Core.Entities;
using VSMS.Infrastructure.Data;

namespace VSMS.Web.Pages.Admin.Import;

public class IndexModel : PageModel
{
    private readonly VsmsDbContext _dbContext;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(VsmsDbContext dbContext, ILogger<IndexModel> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public ImportResult? ImportResults { get; set; }

    public class ImportResult
    {
        public int Total { get; set; }
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(IFormFile? file, bool skipDuplicates = true)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Please select a file to upload.";
            return Page();
        }

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Please upload a CSV file.";
            return Page();
        }

        ImportResults = new ImportResult();

        try
        {
            using var reader = new StreamReader(file.OpenReadStream());
            var headerLine = await reader.ReadLineAsync();

            if (string.IsNullOrEmpty(headerLine))
            {
                TempData["Error"] = "The file is empty.";
                return Page();
            }

            var headers = ParseCsvLine(headerLine);
            var nameIndex = FindColumnIndex(headers, "name");
            var emailIndex = FindColumnIndex(headers, "email");
            var phoneIndex = FindColumnIndex(headers, "phone");
            var isBackupIndex = FindColumnIndex(headers, "isbackup", "backup");

            if (nameIndex < 0 || emailIndex < 0)
            {
                TempData["Error"] = "CSV must have 'Name' and 'Email' columns.";
                return Page();
            }

            var lineNumber = 1;
            string? line;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNumber++;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var fields = ParseCsvLine(line);
                ImportResults.Total++;

                try
                {
                    var name = GetField(fields, nameIndex)?.Trim();
                    var email = GetField(fields, emailIndex)?.Trim();
                    var phone = GetField(fields, phoneIndex)?.Trim();
                    var isBackupStr = GetField(fields, isBackupIndex)?.Trim();

                    if (string.IsNullOrEmpty(name))
                    {
                        ImportResults.Errors.Add($"Line {lineNumber}: Name is required.");
                        continue;
                    }

                    if (string.IsNullOrEmpty(email))
                    {
                        ImportResults.Errors.Add($"Line {lineNumber}: Email is required.");
                        continue;
                    }

                    if (!IsValidEmail(email))
                    {
                        ImportResults.Errors.Add($"Line {lineNumber}: Invalid email format '{email}'.");
                        continue;
                    }

                    // Check for existing
                    var exists = await _dbContext.Volunteers
                        .AnyAsync(v => v.Email.ToLower() == email.ToLower());

                    if (exists)
                    {
                        if (skipDuplicates)
                        {
                            ImportResults.Skipped++;
                            continue;
                        }
                        else
                        {
                            ImportResults.Errors.Add($"Line {lineNumber}: Email '{email}' already exists.");
                            continue;
                        }
                    }

                    var isBackup = ParseBool(isBackupStr);

                    var volunteer = new Volunteer
                    {
                        Name = name,
                        Email = email,
                        Phone = string.IsNullOrEmpty(phone) ? null : phone,
                        IsBackup = isBackup,
                        IsActive = true
                    };

                    _dbContext.Volunteers.Add(volunteer);
                    await _dbContext.SaveChangesAsync();

                    ImportResults.Imported++;
                }
                catch (Exception ex)
                {
                    ImportResults.Errors.Add($"Line {lineNumber}: {ex.Message}");
                    _logger.LogError(ex, "Error importing line {LineNumber}", lineNumber);
                }
            }

            // Log the import
            _dbContext.AuditLogEntries.Add(new AuditLogEntry
            {
                Action = "Data Imported",
                Details = $"Imported {ImportResults.Imported} volunteers from CSV"
            });
            await _dbContext.SaveChangesAsync();

            if (ImportResults.Imported > 0)
            {
                TempData["Success"] = $"Successfully imported {ImportResults.Imported} volunteers.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing CSV file");
            TempData["Error"] = $"Error processing file: {ex.Message}";
        }

        return Page();
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var inQuotes = false;
        var currentField = "";

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    currentField += '"';
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(currentField);
                currentField = "";
            }
            else
            {
                currentField += c;
            }
        }

        result.Add(currentField);
        return result;
    }

    private static int FindColumnIndex(List<string> headers, params string[] names)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i].Trim().ToLower();
            if (names.Any(n => header == n.ToLower()))
                return i;
        }
        return -1;
    }

    private static string? GetField(List<string> fields, int index)
    {
        if (index < 0 || index >= fields.Count)
            return null;
        return fields[index];
    }

    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        value = value.ToLower().Trim();
        return value == "true" || value == "yes" || value == "1" || value == "y";
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var regex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
            return regex.IsMatch(email);
        }
        catch
        {
            return false;
        }
    }
}
