using GtfsNet.Db;
using GtfsNet.OSM;
using GtfsNet.Testing;

new DbConnectionManager(new DirectoryInfo("/Users/ferdi/RiderProjects/GtfsNet/Db"));

var baseDir = "/Users/ferdi/Documents/GTFS Data";
var gtfsPath = Path.Combine(baseDir, "MVV");

var feed = GtfsNet.Gtfs.ReadFromCsv(gtfsPath);
Util.WriteAllBusSegments(feed);
var subLinkShapes = OsmReader.ReadAllSubLinkShapes("/Users/ferdi/Documents/GTFS Data/OSM/BY/Trips/");
Util.GenerateAllConsecutiveShapesAndSetToFeed(feed,subLinkShapes);
