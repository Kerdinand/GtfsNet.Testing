using GtfsNet.Enum;
using GtfsNet.OSM;
using GtfsNet.OSM.Graph;
using GtfsNet.OSM.KdTree;
using GtfsNet.OSM.Routing.OsmStreetRouting;
using GtfsNet.Structs;
using OsmSharp;
using OsmSharp.API;
using QuikGraph.Algorithms.Observers;
using QuikGraph.Algorithms.ShortestPath;

namespace GtfsNet.Testing;

public class Util
{
    public static void RunRouting(GtfsFeed feed)
    {
        
        Console.WriteLine("Looking for indiviual sub trips");

        var osm = new OsmReader("/Users/ferdi/Documents/GTFS Data/OSM/BY","", feed);
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
    }

    public static async void WriteAllBusSegments(GtfsFeed feed)
    {
        var baseDir = "/Users/ferdi/Documents/GTFS Data";

        var osmDir = Path.Combine(baseDir, "OSM", "BY");
        var osmFile = "/input.osm.pbf";

        var osm = new OsmReader(osmDir, osmFile, feed);

        osm.WriteCsv(osmDir, false);
        osm.ReadAndSetOsmDataFromFile();
        osm.SetStopsToStopTimes();
        

        var streetEdges = osm.GetOsmWays(OsmType.HIGHWAY);
        var streetNodes = osm.GetOsmNodes(OsmType.HIGHWAY);
        
        osm.GetOsmWays(OsmType.LIGHTRAIL).Clear();
        osm.GetOsmWays(OsmType.RAIL).Clear();
        osm.GetOsmWays(OsmType.SUBWAY).Clear();
        osm.GetOsmWays(OsmType.TRAM).Clear();
        
        osm.GetOsmNodes(OsmType.LIGHTRAIL).Clear();
        osm.GetOsmNodes(OsmType.RAIL).Clear();
        osm.GetOsmNodes(OsmType.SUBWAY).Clear();
        osm.GetOsmNodes(OsmType.TRAM).Clear();
        
        feed.Calendar.Clear();
        feed.CalendarDate.Clear();
        
        Console.WriteLine("Cleared unused dicts");
        var graph = OsmGraphFactory.BuilCustomOsmGraph(streetNodes, streetEdges);
        Console.WriteLine("Graph contains " + graph.EdgeCount + " edges");
        osm.SetClosestOsmNodeForGtfsStops(graph.Nodes.Values.ToList(), feed.Stops);
        DijkstraManager.Initialize(graph, (byte)Environment.ProcessorCount);
        int counter = 0;
        var allStopIds = osm.GetAllBusStopPairs().Values
            .Chunk(50)
            .Select(chunk => chunk.ToList())
            .ToList();
        int routeNotFound = 0;
        var resultList = new List<List<OsmNode>>();
        foreach (var stopIds in allStopIds)
        {
            var tasks = new List<Task<List<OsmNode>>>();
        
            foreach (var batch in stopIds)
            {
                tasks.Add(DijkstraManager.RunRouteAsync(batch.Item1, batch.Item2));
            }
            var results = await Task.WhenAll(tasks);
            foreach (var task in results)
            {
                if (!(task == null || task.Count == 0 || task.Count < 2))
                {
                    //OsmWriter.SaveNodesAsGeoJsonLine(task, osmDir + $"/Trips/{task[0].Id}-{task[^1].Id}.geojson", 5);
                    resultList.Add(task);
                }
                else
                {
                    routeNotFound++;
                }
            }
            //Console.WriteLine($"{++counter}:{allStopIds.Count}");
        }
        OsmWriter.SaveRoutesAsGeoJsonLines(resultList, "/Users/ferdi/Documents/GTFS Data/OSM/BY" + $"/all.geojson", 5);
        Console.WriteLine($"{routeNotFound} routes  not found");
    }
    
    public static async void MultipleDijkstraSearches(GtfsFeed feed)
    {
        string path = "/Users/ferdi/Documents/GTFS Data/OSM/BY";
        var osm = new OsmReader(path, "/bayern-251226.osm.pbf",feed);
        osm.WriteCsv(path, false);
        osm.ReadAndSetOsmDataFromFile();
        osm.SetStopsToStopTimes();

        var streetEdges = osm.GetOsmWays(OsmType.HIGHWAY);
        var streetNodes = osm.GetOsmNodes(OsmType.HIGHWAY);
        
        Console.WriteLine("Building Graph");
        var graph = OsmGraphFactory.BuilCustomOsmGraph(streetNodes, streetEdges);
        Console.WriteLine("Graph contains " + graph.EdgeCount + " edges");
        osm.SetClosestOsmNodeForGtfsStops(graph.Nodes.Values.ToList(), feed.Stops);
        DijkstraManager.Initialize(graph);
        
        var rnd = Random.Shared;
        var nodeIds = streetNodes.Keys.ToArray();
        while (true)
        {
            var routes = new List<(long src, long dst)>();
            var stops = osm.GetStopsOfRandomBusTrip();
            for (int i = 1; i < stops.Count; i++)
            {
                routes.Add((stops[i - 1].roadNode.Id, stops[i].roadNode.Id));
            }
        
            var tasks = routes
                .Select(r => DijkstraManager.RunRouteAsync(r.src, r.dst))
                .ToArray();
        
            var results = await Task.WhenAll(tasks);


            OsmWriter.SaveRoutesAsGeoJsonLines(results.Where(e => e.Count != 0), path + $"/many/All.geojson");
            
            Console.ReadLine();
        }
    }

    public static void BasicStreetRoute(GtfsFeed feed)
    {
        string path = "/Users/ferdi/Documents/GTFS Data/OSM/BY";
        var osm = new OsmReader(path, "/bayern-251226.osm.pbf",feed);
        osm.WriteCsv(path, true);
        osm.ReadAndSetOsmDataFromFile();

        var streetEdges = osm.GetOsmWays(OsmType.HIGHWAY);
        var streetNodes = osm.GetOsmNodes(OsmType.HIGHWAY);

        
        //Console.WriteLine("Creating KdTree");
        //var kdTree = new OsmKdTree(streetNodes.Values.ToList());
        Console.WriteLine("Building Graph");
        var graph = OsmGraphFactory.BuilCustomOsmGraph(streetNodes, streetEdges);
        var dijkstra = new Dijkstra(0, graph);
        var rnd = Random.Shared;
        while (true)
        {

            var source = streetNodes.Values.ElementAt(rnd.Next(streetNodes.Count));
            var target = streetNodes.Values.ElementAt(rnd.Next(streetNodes.Count));
            Console.WriteLine("Running Dijkstra");

            var result = dijkstra.GetRoute(source.Id, target.Id);
            if (result == null || result.Count == 0)
            {
                Console.WriteLine("No route found");
               
            }
            else
            {
                Console.WriteLine("Route found");
                OsmWriter.SaveNodesAsGeoJsonLine(result, path + "/test.geojson");
            }
            Console.ReadLine();
            
        }
    }
}