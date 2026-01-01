using System.Globalization;
using CsvHelper;
using GtfsNet.Enum;
using GtfsNet.Factories;
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
        osm.SetClosestOsmNodeForGtfsStops(streetEdges, streetNodes, feed.Stops, false);
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
                    OsmWriter.SaveNodesAsCsv(task, osmDir + $"/Trips/{task[0].Id}-{task[^1].Id}.csv", 5);
                    OsmWriter.SaveNodesAsGeoJsonLine(task, osmDir + $"/GeoJson/{task[0].Id}-{task[^1].Id}.geojson", 5);
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

    public static void GenerateAllConsecutiveShapesAndSetToFeed(GtfsFeed feed, Dictionary<string, List<OsmShapeDto>> osmShapes)
    {
        var stopTimes = feed.StopTimesDictionary();
        var result = new List<ShapePoint>();
        var geoJsonResults = new List<List<OsmNode>>();
        int keysDoNotExist = 0;
        int keysExist = 0;
        int shapeId = 0;
        foreach (var trip in feed.Trips)
        {
            var tripStopOsmNodes = stopTimes[trip.Id].Select(e => e.Stop).ToList();
            trip.ShapeId = (shapeId++).ToString();
            int seq = 0;
            for (int i = 1; i < tripStopOsmNodes.Count; i++)
            {
                var tempResult = new List<OsmNode>();
                var key1 =  $"{tripStopOsmNodes[i-1].roadNode.Id}-{tripStopOsmNodes[i].roadNode.Id}";
                var key2 = $"{tripStopOsmNodes[i].roadNode.Id}-{tripStopOsmNodes[i-1].roadNode.Id}";
                if (osmShapes.ContainsKey(key1))
                {
                    foreach (var osmShapeDto in osmShapes[key1])
                    {
                        result.Add(new ShapePoint()
                        {
                            ShapeId = trip.ShapeId,
                            Lat = osmShapeDto.Lat,
                            Lon = osmShapeDto.Lon,
                            DistTraveled = 0,
                            Sequence = seq++
                        });
                        tempResult.Add(new OsmNode()
                        {
                            Id = -1,
                            Lat = osmShapeDto.Lat,
                            Lon = osmShapeDto.Lon,
                        });
                    }
                }
                else
                {
                    if (osmShapes.ContainsKey(key2))
                    {
                        for (int j = osmShapes[key2].Count - 1; j >= 0; j--)
                        {
                            var osmShapeDto = osmShapes[key2][j];
                            result.Add(new ShapePoint()
                            {
                                ShapeId = trip.ShapeId,
                                Lat = osmShapeDto.Lat,
                                Lon = osmShapeDto.Lon,
                                DistTraveled = 0,
                                Sequence = seq++
                            });
                            tempResult.Add(new OsmNode()
                            {
                                Id = -1,
                                Lat = osmShapeDto.Lat,
                                Lon = osmShapeDto.Lon,
                            });
                        }
                    }
                    else
                    {
                        result.Add(new  ShapePoint()
                        {
                            ShapeId = trip.ShapeId,
                            Lat = (float)tripStopOsmNodes[i-1].Lat,
                            Lon = (float)tripStopOsmNodes[i-1].Lon,
                            DistTraveled = 0,
                            Sequence = seq++
                        });
                        tempResult.Add(new OsmNode()
                        {
                            Id = -1,
                            Lat = tripStopOsmNodes[i-1].Lat,
                            Lon = tripStopOsmNodes[i-1].Lon,
                        });
                        result.Add(new ShapePoint()
                        {
                            ShapeId = trip.ShapeId,
                            Lat = (float)tripStopOsmNodes[i].Lat,
                            Lon = (float)tripStopOsmNodes[i].Lon,
                            DistTraveled = 0,
                            Sequence = seq++
                        });
                        tempResult.Add(new OsmNode()
                        {
                            Id = -1,
                            Lat = tripStopOsmNodes[i].Lat,
                            Lon = tripStopOsmNodes[i].Lon,
                        });
                    }
                }
                geoJsonResults.Add(tempResult);
                if (result[^1].ShapeId == result[^2].ShapeId && result[^1].Lat == result[^2].Lat &&
                    result[^1].Lon == result[^2].Lon)
                {
                    result.RemoveAt(result.Count - 1);
                }
            }
        }
        Console.WriteLine($"{result.Count} stops found");
        using (var writer = new StreamWriter("/Users/ferdi/Documents/GTFS Data/OSM/BY/Shapes/shapes.csv"))
        using (var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csvWriter.WriteRecords(result);
        }
        var uniqueRoutes = geoJsonResults
            .Where(r => r.Count >= 2)
            .DistinctBy(r =>
            (
                (Quantize(r[0].Lat), Quantize(r[0].Lon)),
                (Quantize(r[^1].Lat), Quantize(r[^1].Lon))
            ))
            .ToList();


        
        OsmWriter.SaveRoutesAsGeoJsonLines(uniqueRoutes, "/Users/ferdi/Documents/GTFS Data/OSM/BY/Shapes/shapes.geojson");
    }

    static long Quantize(double value, double precision = 1e-6)
        => (long)Math.Round(value / precision);

    
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