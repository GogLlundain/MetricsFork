#Metrics.net

A library and application for reading, parsing and sending WebPagetest, Log4Net, W3C logs, etc to Graphite.

##Log Tail Parser

Read log files from last point, using regex expression to extract the interesting parts.

**Configuration:**

* `location`    - The path to your log files
* `pattern`     - The file mask to search for, default is *
* `graphiteKey` - The key name to use for the calculated stats
  * `{x}` See Mapping section.  Used to do basic string replacements, or replace parts of the key with parts of the log line (regex capture)
* `value`       - [optional] The name of the regex capture to use as the numeric value of the stat.
  * Optional only if `type` is set to `count`.
* `type`        - `avg` or `count`
  * `avg`       - Stats are added up and divided by the number of stat recored within the period defined by `interval`
  * `count`     - Match lines are added up and grouped by the period defined by `interval`
* `interval`    - The regex capture which defines the date/time value of this stat
* `dateFormat`  - The format `interval` is in
* `maxTailMB`   - [optional] The maximum amount of data to read from a file.  If the blank or zero, all data is read from the last point read
* `Mapping`     - Key value pairs
  * `key`       - Matched with values in graphiteKey with braces.  key="0" - {0}
  * `value`     - Value to replace the mapping key with.  Prefixed with ? will match a regex match, anything else will be replaced exactly

```xml
<Log location="d:\projects\" pattern="*.log" graphiteKey="timers.iis.{0}.prosvc.{1}" value="timetaken" type="avg"
     interval="datetime" dateFormat="yyyy-MM-dd HH:mm:ss" maxTailMB="10"
     regex="^(?&lt;datetime&gt;\S+\s\S+)\s\S+\s\S+\s\S+\s/(?&lt;url&gt;[^+/]*)\S*\s\S+\s\S+\s\S+\s\S+\s\S+\s(?&lt;code&gt;\S+)\s\S+\s\S+\s(?&lt;timetaken&gt;\S+)$">
  <Mapping>
    <add key="0" value="server1"/>
    <add key="1" value="?url"/>
  </Mapping>
</Log>
```

##WebPagetest Parser

[incomplete] WebPagetest  - Read test runs from a private instance and graph all numeric results for First View, Repeat View and per run.  

Configuration format to follow
