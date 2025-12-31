using GtfsNet.Testing;

var baseDir = "/Users/ferdi/Documents/GTFS Data";
var gtfsPath = Path.Combine(baseDir, "MVV");

var feed = GtfsNet.Gtfs.ReadFromCsv(gtfsPath);
Util.WriteAllBusSegments(feed);