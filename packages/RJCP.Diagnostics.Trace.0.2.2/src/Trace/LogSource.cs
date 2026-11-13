namespace RJCP.Diagnostics.Trace
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

#if NET6_0_OR_GREATER
    using Microsoft.Extensions.Logging;
#endif

#if NET10_0_OR_GREATER
    using Lock = System.Threading.Lock;
#else
    using Lock = System.Object;
#endif

    /// <summary>
    /// A <see cref="TraceSource"/> wrapper for better performance tracing.
    /// </summary>
    /// <remarks>
    /// Use the <see cref="LogSource"/> class instead of a <see cref="System.Diagnostics.TraceSource"/> class to
    /// initialize logging, so that tests can be done with the <see cref="ShouldTrace(TraceEventType)"/> method.
    /// <para>
    /// The goal is to allow classes to test if tracing is enabled or not, before using the <see cref="TraceSource"/>.
    /// This can help prevention of unnecessary objects on the heap by tracing only when tracing is needed.
    /// </para>
    /// <code language="csharp">
    ///<![CDATA[
    ///private static LogSource log = new LogSource("MyTrace");
    ///if (log.ShouldTrace(TraceEventType.Information))
    ///  log.TraceSource.TraceEvent(TraceEventType.Information, id, $"Log Message: {errorCode}");
    ///]]>
    /// </code>
    /// <para>
    /// This allows first a check if tracing is allowed or not, by doing a bitwise comparison, and only then if tracing
    /// is enabled, will the TraceEvent function be called thus allocating the <c>object[]</c> for params, then boxing
    /// the integer value <c>errorCode</c> into the <c>object[]</c> array.
    /// </para>
    /// <para>Initialisation is simple with .NET Framework, as the framework provides its own singleton implementation:</para>
    /// <code language="csharp">
    ///<![CDATA[
    ///using RJCP.Diagnostics.Trace;
    ///
    ///public static class Log {
    ///  public static LogSource Data { get; }
    ///  
    ///  static Log() {
    ///    Data = new LogSource("RJCP.Data");
    ///  }
    ///}
    ///]]>
    /// </code>
    /// <para>Most of the work for .NET Framework is in the configuration file on how to log and where:</para>
    /// <code language="xml">
    ///<![CDATA[
    /// <?xml version="1.0" encoding="utf-8" ?>
    /// <configuration>
    ///   <startup>
    ///     <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.0" />
    ///   </startup>
    /// 
    ///   <system.diagnostics>
    ///     <sharedListeners>
    ///       <add name="console" type="System.Diagnostics.ConsoleTraceListener"/>
    ///     </sharedListeners>
    /// 
    ///     <sources>
    ///       <source name="RJCP.Data" switchValue="Verbose">
    ///         <listeners>
    ///           <clear/>
    ///           <add name="console"/>
    ///         </listeners>
    ///       </source>
    ///     </sources>
    ///     <trace autoflush="true" useGlobalLock="false"/>
    ///   </system.diagnostics>
    /// </configuration>
    /// ]]>
    /// </code>
    /// <para>If you've got a .NET core application, you can initialise the <see cref="LogSource"/> as:</para>
    /// <code language="csharp">
    ///<![CDATA[
    ///using Microsoft.Extensions.Configuration;
    ///using Microsoft.Extensions.Logging;
    ///using RJCP.Diagnostics.Trace;
    ///
    ///public static class Log {
    ///  public static LogSource Data { get; }
    ///  
    ///  static Log() {
    ///    LogSource.SetLoggerFactory(GetLoggerFactory());
    ///    Data = new LogSource("RJCP.Data");
    ///  }
    ///  
    ///  private static ILoggerFactory GetLoggerFactory() {
    ///    IConfigurationRoot config = new ConfigurationBuilder()
    ///      .AddJsonFile("appsettings.log.json", true, false)
    ///      .Build();
    ///    return LoggerFactory.Create(builder => {
    ///      builder.AddConfiguration(config.GetSection("Logging"))
    ///             .AddConsole();
    ///    });
    ///  }
    ///}
    ///]]>
    /// </code>
    /// The file <c>appsettings.log.json</c> might look like:
    /// <code language="json">
    ///<![CDATA[
    ///{
    ///  "Logging": {
    ///    "LogLevel": {
    ///      "Default": "Error"
    ///    },
    ///    "Console": {
    ///      "IncludeScopes": true,
    ///      "LogLevel": {
    ///        "RJCP.Data": "Debug"
    ///      }
    ///    }
    ///  }
    ///}
    ///]]>
    /// </code>
    /// </remarks>
    public sealed class LogSource : IDisposable
    {
        private readonly long m_TraceLevels;
        private TraceSource m_TraceSource;

        private static readonly Lock TraceSourceLock = new();
        private static readonly Dictionary<string, TraceSource> TraceSources = new();

        /// <summary>
        /// Sets the Log Source for a specific <paramref name="source"/>.
        /// </summary>
        /// <param name="source">
        /// The <see cref="System.Diagnostics.TraceSource"/> that should be returned when queried.
        /// </param>
        /// <returns>
        /// If the <paramref name="source"/> was already defined (based on the category given in
        /// <see cref="TraceSource.Name"/>), returns <see langword="true"/> indicating it was overwritten, else
        /// <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// Use this method to override setting a <see cref="System.Diagnostics.TraceSource"/> for a specific category
        /// before it is requested, as an alternative to using a configuration.
        /// </remarks>
        public static bool SetLogSource(TraceSource source)
        {
            ThrowHelper.ThrowIfNull(source);

            lock (TraceSourceLock) {
                string name = source.Name;
                if (TraceSources.ContainsKey(name)) {
                    TraceSources[name] = source;
                    return true;
                }
                TraceSources.Add(name, source);
                return false;
            }
        }

        /// <summary>
        /// Sets the Log Source for a specific <paramref name="name"/> to a particular <paramref name="listener"/>.
        /// </summary>
        /// <param name="name">The category name of the log source.</param>
        /// <param name="level">The switch trace level.</param>
        /// <param name="listener">The trace listener.</param>
        /// <returns>
        /// If the <paramref name="name"/> was already defined, returns <see langword="true"/> indicating it was
        /// overwritten, else <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="name"/> is <see langword="null"/>.
        /// <para>- or -</para>
        /// <paramref name="listener"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is an empty string.</exception>
        /// <remarks>
        /// Use this method to override setting a <see cref="System.Diagnostics.TraceSource"/> for a specific category
        /// before it is requested, as an alternative to using a configuration.
        /// </remarks>
        public static bool SetLogSource(string name, SourceLevels level, TraceListener listener)
        {
            ThrowHelper.ThrowIfNullOrEmpty(name);
            ThrowHelper.ThrowIfNull(listener);

            TraceSource source = new(name) {
                Switch = new SourceSwitch(name) {
                    Level = level
                }
            };
            source.Listeners.Clear();
            source.Listeners.Add(listener);

            lock (TraceSourceLock) {
                if (TraceSources.ContainsKey(name)) {
                    TraceSources[name] = source;
                    return true;
                }
                TraceSources.Add(name, source);
                return false;
            }
        }

#if NET6_0_OR_GREATER
        private static ILoggerFactory LoggerFactory;
        private static readonly Dictionary<string, ILogger> Loggers = new();

        /// <summary>
        /// Remove a <see cref="ILoggerFactory"/> if set with <see cref="SetLoggerFactory(ILoggerFactory)"/>.
        /// </summary>
        /// <returns>
        /// If the logger factory was already defined, returns <see langword="true"/> indicating it was overwritten, else
        /// <see langword="false"/>.
        /// </returns>
        public static bool ClearLoggerFactory()
        {
            lock (TraceSourceLock) {
                bool predefined = LoggerFactory is not null;
                LoggerFactory = null;
                return predefined;
            }
        }

        /// <summary>
        /// Set a Logger Factory to initialize a TraceFactory for a given category.
        /// </summary>
        /// <param name="loggerFactory">
        /// The <see cref="ILoggerFactory"/> that can be used to create an <see cref="ILogger"/> for a given category.
        /// </param>
        /// <returns>
        /// If the logger factory was already defined, returns <see langword="true"/> indicating it was not overwritten,
        /// else <see langword="false"/>.
        /// </returns>
        [CLSCompliant(false)]
        public static bool SetLoggerFactory(ILoggerFactory loggerFactory)
        {
            return SetLoggerFactory(loggerFactory, false);
        }

        /// <summary>
        /// Set a Logger Factory to initialize a TraceFactory for a given category.
        /// </summary>
        /// <param name="loggerFactory">
        /// The <see cref="ILoggerFactory"/> that can be used to create an <see cref="ILogger"/> for a given category.
        /// </param>
        /// <param name="overrideFactory">
        /// Set to <see langword="true"/> to override the factory if it is already set. If <see langword="false"/>, then
        /// the existing factory is not overridden.
        /// </param>
        /// <returns>
        /// If the logger factory was already defined, returns <see langword="true"/>. If
        /// <paramref name="overrideFactory"/> is <see langword="true"/>, then it is overwritten. else
        /// <see langword="false"/>.
        /// </returns>
        [CLSCompliant(false)]
        public static bool SetLoggerFactory(ILoggerFactory loggerFactory, bool overrideFactory)
        {
            ThrowHelper.ThrowIfNull(loggerFactory);
            lock (TraceSourceLock) {
                bool predefined = LoggerFactory is not null;
                if (!predefined || overrideFactory) LoggerFactory = loggerFactory;
                return predefined;
            }
        }

        /// <summary>
        /// Sets the Log Source for a specific <paramref name="name"/> to a particular <paramref name="logger"/>.
        /// </summary>
        /// <param name="name">The category name of the log source.</param>
        /// <param name="logger">
        /// The <see cref="ILogger"/> that should be wrapped around a <see cref="System.Diagnostics.TraceSource"/>.
        /// </param>
        /// <returns>
        /// If the <paramref name="name"/> was already defined, returns <see langword="true"/> indicating it was
        /// overwritten, else <see langword="false"/>.
        /// </returns>
        [CLSCompliant(false)]
        public static bool SetLogSource(string name, ILogger logger)
        {
            ThrowHelper.ThrowIfNullOrEmpty(name);
            ThrowHelper.ThrowIfNull(logger);

            TraceSource source = new(name) {
                Switch = new SourceSwitch(name) {
                    Level = GetSourceLevels(logger)
                }
            };
            source.Listeners.Clear();
            source.Listeners.Add(new LoggerTraceListener(name, logger));

            lock (TraceSourceLock) {
                if (TraceSources.ContainsKey(name)) {
                    Loggers[name] = logger;
                    TraceSources[name] = source;
                    return true;
                }
                Loggers.Add(name, logger);
                TraceSources.Add(name, source);
                return false;
            }
        }

        private static SourceLevels GetSourceLevels(ILogger logger)
        {
            if (logger is null) return SourceLevels.Off;

            if (logger.IsEnabled(LogLevel.Trace)) return SourceLevels.All;
            if (logger.IsEnabled(LogLevel.Debug)) return SourceLevels.Verbose;
            if (logger.IsEnabled(LogLevel.Information)) return SourceLevels.Information;
            if (logger.IsEnabled(LogLevel.Warning)) return SourceLevels.Warning;
            if (logger.IsEnabled(LogLevel.Error)) return SourceLevels.Error;
            if (logger.IsEnabled(LogLevel.Critical)) return SourceLevels.Critical;
            return SourceLevels.Off;
        }
#endif

        /// <summary>
        /// Clears all cached log sources
        /// </summary>
        public static void ClearLogSource()
        {
            lock (TraceSourceLock) {
                TraceSources.Clear();
#if NET6_0_OR_GREATER
                Loggers.Clear();
#endif
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LogSource"/> class that doesn't log.
        /// </summary>
        /// <remarks>
        /// Provides a constructor for a logger object that doesn't log. The implementation is not completely empty, a
        /// <see cref="TraceSource"/> is still available but effectively logging is disabled. It is possible to
        /// manipulate the <see cref="TraceSource"/> property to enable logging. The name of the trace source is
        /// <c>default</c>.
        /// </remarks>
        public LogSource()
        {
            m_TraceSource = new TraceSource("default") {
                Switch = new SourceSwitch("default") {
                    Level = SourceLevels.Off
                }
            };
            m_TraceSource.Listeners.Clear();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LogSource"/> class with the
        /// <see cref="System.Diagnostics.TraceSource"/> of the name provided.
        /// </summary>
        /// <param name="name">The name of the trace source.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="name"/> may not be <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> may not be empty.</exception>
        public LogSource(string name)
        {
            ThrowHelper.ThrowIfNullOrEmpty(name);

            lock (TraceSourceLock) {
                InitLogSource(name);
                m_TraceLevels = GetTraceLevels(m_TraceSource);
            }
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// Create a
        /// </summary>
        /// <param name="name">
        /// The name of the logger that should be used. This can be the name of your library for example.
        /// </param>
        /// <param name="logger">
        /// The <see cref="ILogger"/> that should be wrapped within a <see cref="System.Diagnostics.TraceSource"/>
        /// object for logging.
        /// </param>
        /// <remarks>
        /// This constructor is to create a logger directly from your own constructor. It is not registered and used
        /// directly as it is, which is useful for .NET Core code that takes its logger through the constructor.
        /// </remarks>
        [CLSCompliant(false)]
        public LogSource(string name, ILogger logger)
        {
            ThrowHelper.ThrowIfNullOrEmpty(name);
            ThrowHelper.ThrowIfNull(logger);

            m_TraceSource = CreateFromLogger(name, logger);
            Logger = logger;
            m_TraceLevels = GetTraceLevels(m_TraceSource);
        }

        private static TraceSource CreateFromLogger(string name, ILogger logger)
        {
            TraceSource source = new(name) {
                Switch = new SourceSwitch(name) {
                    Level = GetSourceLevels(logger)
                }
            };
            source.Listeners.Clear();
            source.Listeners.Add(new LoggerTraceListener(name, logger));
            return source;
        }
#endif

        private void InitLogSource(string name)
        {
#if NETFRAMEWORK
            if (!TraceSources.TryGetValue(name, out m_TraceSource)) {
                m_TraceSource = new TraceSource(name);
                TraceSources.Add(name, m_TraceSource);
            }
#else
            if (!TraceSources.TryGetValue(name, out m_TraceSource)) {
                if (LoggerFactory is not null) {
                    ILogger logger = LoggerFactory.CreateLogger(name);
                    if (logger is not null) {
                        m_TraceSource = CreateFromLogger(name, logger);
                        Logger = logger;
                        Loggers.Add(name, logger);
                    } else {
                        m_TraceSource = new TraceSource(name);
                    }
                } else {
                    m_TraceSource = new TraceSource(name);
                }
                TraceSources.Add(name, m_TraceSource);
            } else {
                if (Loggers.TryGetValue(name, out ILogger cachedLogger)) {
                    Logger = cachedLogger;
                }
            }
#endif
        }

        /// <summary>
        /// Gets the trace source.
        /// </summary>
        /// <value>The trace source object.</value>
        /// <exception cref="ObjectDisposedException"/>
        public TraceSource TraceSource
        {
            get
            {
                ThrowHelper.ThrowIfDisposed(m_TraceSource is null, this);
                return m_TraceSource;
            }
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// If there is an <see cref="ILogger"/> associated with this instance, get it.
        /// </summary>
        [CLSCompliant(false)]
        public ILogger Logger { get; private set; }
#endif

        private static long GetTraceLevels(TraceSource source)
        {
            long levels = 0;
            if (source.Switch.ShouldTrace(TraceEventType.Critical)) levels |= (long)TraceEventType.Critical;
            if (source.Switch.ShouldTrace(TraceEventType.Error)) levels |= (long)TraceEventType.Error;
            if (source.Switch.ShouldTrace(TraceEventType.Warning)) levels |= (long)TraceEventType.Warning;
            if (source.Switch.ShouldTrace(TraceEventType.Information)) levels |= (long)TraceEventType.Information;
            if (source.Switch.ShouldTrace(TraceEventType.Verbose)) levels |= (long)TraceEventType.Verbose;
#if NETFRAMEWORK
            if (source.Switch.ShouldTrace(TraceEventType.Start)) levels |= (long)TraceEventType.Start;
            if (source.Switch.ShouldTrace(TraceEventType.Stop)) levels |= (long)TraceEventType.Stop;
            if (source.Switch.ShouldTrace(TraceEventType.Suspend)) levels |= (long)TraceEventType.Suspend;
            if (source.Switch.ShouldTrace(TraceEventType.Resume)) levels |= (long)TraceEventType.Resume;
            if (source.Switch.ShouldTrace(TraceEventType.Transfer)) levels |= (long)TraceEventType.Transfer;
#endif

            return levels;
        }

        /// <summary>
        /// Tests if tracing should be done for the log level provided.
        /// </summary>
        /// <param name="eventType">Type of the event.</param>
        /// <returns><see langword="true"/> if tracing is enabled; otherwise <see langword="false"/>.</returns>
        /// <remarks>Note, changing the trace source log level on the fly will not result in an update here.</remarks>
        public bool ShouldTrace(TraceEventType eventType)
        {
            return ((m_TraceLevels & (long)eventType) != 0);
        }

        /// <summary>
        /// Writes a trace event message to the trace listeners in the <see cref="TraceSource.Listeners"/> collection.
        /// </summary>
        /// <param name="eventType">
        /// One of the enumeration values that specifies the event type of the trace data.
        /// </param>
        /// <param name="message">The trace message to write.</param>
        public void TraceEvent(TraceEventType eventType, string message)
        {
            TraceSource.TraceEvent(eventType, 0, message);
        }

        /// <summary>
        /// Writes a trace event message to the trace listeners in the <see cref="TraceSource.Listeners"/> collection.
        /// </summary>
        /// <param name="eventType">
        /// One of the enumeration values that specifies the event type of the trace data.
        /// </param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="message">The trace message to write.</param>
        public void TraceEvent(TraceEventType eventType, int id, string message)
        {
            TraceSource.TraceEvent(eventType, id, message);
        }

        /// <summary>
        /// Writes a trace event message to the trace listeners in the <see cref="TraceSource.Listeners"/> collection.
        /// </summary>
        /// <param name="eventType">Type of the event.</param>
        /// <param name="format">
        /// A composite format string that contains text intermixed with zero or more format items, which correspond to
        /// objects in the <paramref name="args"/> array.
        /// </param>
        /// <param name="args">An object array containing zero or more objects to format.</param>
        public void TraceEvent(TraceEventType eventType, string format, params object[] args)
        {
            TraceSource.TraceEvent(eventType, 0, format, args);
        }

        /// <summary>
        /// Writes a trace event message to the trace listeners in the <see cref="TraceSource.Listeners"/> collection.
        /// </summary>
        /// <param name="eventType">Type of the event.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="format">
        /// A composite format string that contains text intermixed with zero or more format items, which correspond to
        /// objects in the <paramref name="args"/> array.
        /// </param>
        /// <param name="args">An object array containing zero or more objects to format.</param>
        public void TraceEvent(TraceEventType eventType, int id, string format, params object[] args)
        {
            TraceSource.TraceEvent(eventType, id, format, args);
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="e">The exception to trace.</param>
        /// <param name="message">The message to trace in addition.</param>
        public void TraceException(Exception e, string message)
        {
            TraceException(e, null, 0, message);
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="e">The exception to trace.</param>
        /// <param name="function">The function where the exception occurred.</param>
        /// <param name="message">The message to trace in addition.</param>
        public void TraceException(Exception e, string function, string message)
        {
            TraceException(e, function, 0, message);
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="e">The exception to trace.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="message">The message to trace in addition.</param>
        public void TraceException(Exception e, int id, string message)
        {
            TraceException(e, null, id, message);
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="e">The exception to trace.</param>
        /// <param name="function">The function where the exception occurred.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="message">The message to trace in addition.</param>
        public void TraceException(Exception e, string function, int id, string message)
        {
            string logMessage;
            if (ShouldTrace(TraceEventType.Information)) {
                if (function is not null) {
                    logMessage = string.Format("{0}: {1}{3}{2}", function, message, e, Environment.NewLine);
                } else {
                    logMessage = string.Format("{0}{2}{1}", message, e, Environment.NewLine);
                }
                TraceSource.TraceEvent(TraceEventType.Error, id, logMessage);
            } else if (ShouldTrace(TraceEventType.Error)) {
                if (function is not null) {
                    logMessage = string.Format("{0}: {1}{3}{2}", function, message, e.Message, Environment.NewLine);
                } else {
                    logMessage = string.Format("{0}{2}{1}", message, e.Message, Environment.NewLine);
                }
                TraceSource.TraceEvent(TraceEventType.Error, id, logMessage);
            }
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="e">The exception to trace.</param>
        /// <param name="format">The message to trace in addition.</param>
        /// <param name="args">The arguments used for formatting within <paramref name="format"/>.</param>
        public void TraceException(Exception e, string format, params object[] args)
        {
            TraceException(e, null, 0, string.Format(format, args));
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="e">The exception to trace.</param>
        /// <param name="function">The function where the exception occurred.</param>
        /// <param name="format">The message to trace in addition.</param>
        /// <param name="args">The arguments used for formatting within <paramref name="format"/>.</param>
        public void TraceException(Exception e, string function, string format, params object[] args)
        {
            TraceException(e, function, 0, string.Format(format, args));
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="e">The exception to trace.</param>
        /// <param name="format">The message to trace in addition.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="args">The arguments used for formatting within <paramref name="format"/>.</param>
        public void TraceException(Exception e, int id, string format, params object[] args)
        {
            TraceException(e, null, id, string.Format(format, args));
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="e">The exception to trace.</param>
        /// <param name="function">The function where the exception occurred.</param>
        /// <param name="format">The message to trace in addition.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="args">The arguments used for formatting within <paramref name="format"/>.</param>
        public void TraceException(Exception e, string function, int id, string format, params object[] args)
        {
            TraceException(e, function, id, string.Format(format, args));
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="name">The name of the object.</param>
        /// <param name="e">The exception to trace.</param>
        /// <param name="message">The message to trace in addition.</param>
        public void TraceException(string name, Exception e, string message)
        {
            TraceException(name, e, null, 0, message);
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="name">The name of the object.</param>
        /// <param name="e">The exception to trace.</param>
        /// <param name="function">The function where the exception occurred.</param>
        /// <param name="message">The message to trace in addition.</param>
        public void TraceException(string name, Exception e, string function, string message)
        {
            TraceException(name, e, function, 0, message);
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="name">The name of the object.</param>
        /// <param name="e">The exception to trace.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="message">The message to trace in addition.</param>
        public void TraceException(string name, Exception e, int id, string message)
        {
            TraceException(name, e, null, id, message);
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="name">The name of the object.</param>
        /// <param name="e">The exception to trace.</param>
        /// <param name="function">The function where the exception occurred.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="message">The message to trace in addition.</param>
        public void TraceException(string name, Exception e, string function, int id, string message)
        {
            string logMessage;
            if (ShouldTrace(TraceEventType.Information)) {
                if (function is not null) {
                    logMessage = string.Format("{0}: {1}: {2}{4}{3}", name, function, message, e, Environment.NewLine);
                } else {
                    logMessage = string.Format("{0}: {1}{3}{2}", name, message, e, Environment.NewLine);
                }
                TraceSource.TraceEvent(TraceEventType.Error, id, logMessage);
            } else if (ShouldTrace(TraceEventType.Error)) {
                if (function is not null) {
                    logMessage = string.Format("{0}: {1}: {2}{4}{3}", name, function, message, e.Message, Environment.NewLine);
                } else {
                    logMessage = string.Format("{0}: {1}{3}{2}", name, message, e.Message, Environment.NewLine);
                }
                TraceSource.TraceEvent(TraceEventType.Error, id, logMessage);
            }
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="name">The name of the object.</param>
        /// <param name="e">The exception to trace.</param>
        /// <param name="function">The function where the exception occurred.</param>
        /// <param name="format">The message to trace in addition.</param>
        /// <param name="args">The arguments used for formatting within <paramref name="format"/>.</param>
        public void TraceException(string name, Exception e, string function, string format, params object[] args)
        {
            TraceException(name, e, function, 0, string.Format(format, args));
        }

        /// <summary>
        /// Traces the exception with a level of detail based on the trace level.
        /// </summary>
        /// <param name="name">The name of the object.</param>
        /// <param name="e">The exception to trace.</param>
        /// <param name="function">The function where the exception occurred.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="format">The message to trace in addition.</param>
        /// <param name="args">The arguments used for formatting within <paramref name="format"/>.</param>
        public void TraceException(string name, Exception e, string function, int id, string format, params object[] args)
        {
            TraceException(name, e, function, id, string.Format(format, args));
        }

        private bool m_IsDisposed;

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting managed and unmanaged
        /// resources.
        /// </summary>
        public void Dispose()
        {
            if (m_IsDisposed) return;

            m_TraceSource.Flush();
            m_TraceSource.Close();
            m_TraceSource = null;
            m_IsDisposed = true;
        }
    }
}
