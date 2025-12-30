using GtfsNet.Factories;
using GtfsNet.Functions;
using GtfsNet.OSM;
using GtfsNet.OSM.KdTree;
using GtfsNet.OSM.Rail;
using GtfsNet.Structs;


var feed = GtfsNet.Gtfs.ReadFromCsv("/Users/ferdi/Documents/GTFS Data/MVV/");

Console.WriteLine("Looking for indiviual sub trips");

var osm = new OsmReader("/Users/ferdi/Documents/GTFS Data/OSM/BY", feed);
osm.WriteCsv("/Users/ferdi/Documents/GTFS Data/OSM/BY");
osm.SetGraphs();
osm.ReadSubLinks();

var routeDict = feed.Route.ToDictionary(r => r.Id);

while (true)
{
    Trip randomTrip;
    var resultList = osm.ComputeLines(randomTrip = feed.GetRandomRailTrip());
    var result = (resultList[0].Count > resultList[1].Count && resultList[1].Count == 0) ? resultList[0] : resultList[1];
    Console.WriteLine(routeDict[randomTrip.RouteId].ShortName);
    OsmWriter.SaveNodesAsGeoJsonLine(resultList[0], "/Users/ferdi/Documents/GTFS Data/OSM/BY/shape_lines.geojson");

    if (resultList[1].Count != 0)
    {
        OsmWriter.SaveNodesAsGeoJsonLine(resultList[1], "/Users/ferdi/Documents/GTFS Data/OSM/BY/shape_lines1.geojson");

    }
    
    while (true)
    {
        if (Console.ReadLine() == String.Empty)
            break;
    }
}