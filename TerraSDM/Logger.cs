using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace TerraSDM;

/// <summary>
/// Custom file logger provider for TerraSDM
/// </summary>
public class FileLoggerProvider : ILoggerProvider
{
	private readonly string _logFilePath;
	private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
	private readonly StreamWriter _writer;
	private readonly object _lock = new();

	public FileLoggerProvider(string logFilePath)
	{
		_logFilePath = logFilePath;

		// Ensure directory exists
		var directory = Path.GetDirectoryName(logFilePath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}

		// Open log file for appending
		_writer = new StreamWriter(_logFilePath, append: true) { AutoFlush = true };
	}

	public ILogger CreateLogger(string categoryName)
	{
		return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _writer, _lock));
	}

	public void Dispose()
	{
		_loggers.Clear();
		_writer?.Dispose();
	}
}

/// <summary>
/// File logger implementation
/// </summary>
public class FileLogger : ILogger
{
	private readonly string _categoryName;
	private readonly StreamWriter _writer;
	private readonly object _lock;

	public FileLogger(string categoryName, StreamWriter writer, object lockObj)
	{
		_categoryName = categoryName;
		_writer = writer;
		_lock = lockObj;
	}

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		if (!IsEnabled(logLevel))
			return;

		var logLevelString = logLevel switch
		{
			LogLevel.Debug => "DEBUG",
			LogLevel.Information => "INFO ",
			LogLevel.Warning => "WARN ",
			LogLevel.Error => "ERROR",
			LogLevel.Critical => "FATAL",
			_ => "     "
		};

		var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		var message = formatter(state, exception);
		var logEntry = $"[{timestamp}] [{logLevelString}] [{_categoryName}] {message}";

		if (exception != null)
		{
			logEntry += $"\n{exception}";
		}

		lock (_lock)
		{
			_writer.WriteLine(logEntry);
		}
	}
}

/// <summary>
/// Static logging helper for TerraSDM application
/// </summary>
public static class Logger
{
	private static ILoggerFactory? _loggerFactory;
	private static readonly string _logFilePath = Path.Combine(Util.StorePath, "terrasdm.log");

	/// <summary>
	/// Initializes the logging system
	/// </summary>
	public static void Initialize()
	{
		if (_loggerFactory != null)
			return;

		_loggerFactory = LoggerFactory.Create(builder =>
		{
#if DEBUG
			// Console logging in debug mode
			builder.AddConsole();
			builder.SetMinimumLevel(LogLevel.Debug);
#else
			// Less verbose in release mode
			builder.SetMinimumLevel(LogLevel.Information);
#endif

			// File logging always enabled
			builder.AddProvider(new FileLoggerProvider(_logFilePath));

			// Filter out noisy Uno/Microsoft logs
			builder.AddFilter("Uno", LogLevel.Warning);
			builder.AddFilter("Windows", LogLevel.Warning);
			builder.AddFilter("Microsoft", LogLevel.Warning);
		});

		Info("TerraSDM", "Logging system initialized");
	}

	/// <summary>
	/// Gets or creates a logger for the specified category
	/// </summary>
	private static ILogger GetLogger(string category)
	{
		if (_loggerFactory == null)
			Initialize();

		return _loggerFactory!.CreateLogger(category);
	}

	/// <summary>
	/// Logs a debug message (lowest severity - detailed diagnostic information)
	/// </summary>
	public static void Debug(string category, string message)
	{
		GetLogger(category).LogDebug(message);
	}

	/// <summary>
	/// Logs a debug message with exception
	/// </summary>
	public static void Debug(string category, string message, Exception ex)
	{
		GetLogger(category).LogDebug(ex, message);
	}

	/// <summary>
	/// Logs an informational message (general information about application flow)
	/// </summary>
	public static void Info(string category, string message)
	{
		GetLogger(category).LogInformation(message);
	}

	/// <summary>
	/// Logs an informational message with exception
	/// </summary>
	public static void Info(string category, string message, Exception ex)
	{
		GetLogger(category).LogInformation(ex, message);
	}

	/// <summary>
	/// Logs a warning message (unexpected events that don't stop execution)
	/// </summary>
	public static void Warning(string category, string message)
	{
		GetLogger(category).LogWarning(message);
	}

	/// <summary>
	/// Logs a warning message with exception
	/// </summary>
	public static void Warning(string category, string message, Exception ex)
	{
		GetLogger(category).LogWarning(ex, message);
	}

	/// <summary>
	/// Logs an error message (errors and exceptions that affect functionality)
	/// </summary>
	public static void Error(string category, string message)
	{
		GetLogger(category).LogError(message);
	}

	/// <summary>
	/// Logs an error message with exception
	/// </summary>
	public static void Error(string category, string message, Exception ex)
	{
		GetLogger(category).LogError(ex, message);
	}

	/// <summary>
	/// Clears the log file
	/// </summary>
	public static void ClearLog()
	{
		try
		{
			if (File.Exists(_logFilePath))
			{
				File.Delete(_logFilePath);
			}
		}
		catch (Exception ex)
		{
			Error("Logger", "Failed to clear log file", ex);
		}
	}

	/// <summary>
	/// Gets the path to the log file
	/// </summary>
	public static string GetLogFilePath() => _logFilePath;
}
