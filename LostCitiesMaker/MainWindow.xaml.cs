using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Windows.Controls;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;
using Color = System.Drawing.Color;
using Image = System.Drawing.Image;

namespace LostCitiesMaker;

public partial class MainWindow
{

    
    private ushort _viewDirection = 5;
    private string _currentPartName = "Root";

    private static readonly ushort[][] ProjectionPlanes = 
    {
        new ushort[] { 2, 1 }, // 0, E, +X = 21
        new ushort[] { 3, 5 }, // 1, U, +Y = 35
        new ushort[] { 3, 1 }, // 2, S, +Z = 31
        new ushort[] { 5, 1 }, // 3, W, -X = 51
        new ushort[] { 3, 2 }, // 4, D, -Y = 32
        new ushort[] { 0, 1 }  // 5, N, -Z = 01
    };

    private static readonly Color FogColor = Color.FromArgb(32, 255, 255, 255);
    
    public MainWindow()
    {
        InitializeComponent();
        
        RenderOptions.SetBitmapScalingMode(Editor, BitmapScalingMode.NearestNeighbor);

        LoadData();
    }
    
    /// <summary>
    /// Huge method that runs on startup, loading all of the assets and data into the Data class
    /// </summary>
    private void LoadData()
    {
        var index = 0;

        const string assetsPath = @"./assets.zip";
        const string dataPath = @"./data.zip";

        #region Assets // for rendering (and block discovery)
        
        Dictionary<string, string> blockStates = new();
        //Dictionary<string, JObject> models = new();


        using (var archive = ZipFile.OpenRead(assetsPath))
        {
            foreach (var entry in archive.Entries)
            {
                var filename = entry.Name;

                if (entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    var name = filename[..^4];
                    var path = entry.FullName[..^4];

                    var image = (Bitmap)Image.FromStream(entry.Open());

                    
                    
                    var pathCopy = path;

                    var sepIndex1 = pathCopy.IndexOf("/", StringComparison.Ordinal);
                    
                    pathCopy = (sepIndex1 < 0)
                        ? pathCopy
                        : pathCopy.Remove(sepIndex1, 1);
                    var sepIndex2 = pathCopy.IndexOf("/", StringComparison.Ordinal);
                    
                    pathCopy = (sepIndex2 < 0)
                        ? pathCopy
                        : pathCopy.Remove(sepIndex2, 1);
                    var sepIndex3 = pathCopy.IndexOf("/", StringComparison.Ordinal);
                    
                    
                    var modId = path.AsSpan(0, sepIndex1).ToString(); // ModID
                    var type = path.AsSpan(sepIndex2 + 2, sepIndex3 - sepIndex2).ToString(); // Type
                    var assetName = string.Concat(modId, ":", type, "/", name);
                    
                    // Console.WriteLine(assetName);
                    Assets.Textures.TryAdd(assetName, image);


                    index++;
                }

                if (entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var name = filename[..^5];
                    
                    var path = entry.FullName[..^5];
                    var sepIndex1 = path.IndexOf("/", StringComparison.Ordinal);
                    var modId = path.AsSpan(0, sepIndex1).ToString(); // ModID


                    string fileContents;
                    using (var reader = new StreamReader(entry.Open()))
                    {
                        fileContents = reader.ReadToEnd();
                    }

                    // blockstate
                    if (entry.FullName.StartsWith(modId + "/blockstates/", StringComparison.OrdinalIgnoreCase))
                    {
                        var contents = JObject.Parse(fileContents);

                        // Variants
                        if (contents.TryGetValue("variants", out var key1))
                        {
                            foreach (var kvp in (JObject)key1)
                            {
                                var stateName = kvp.Key != "" ? name + "[" + kvp.Key + "]" : name;

                                var type = kvp.Value.Type;

                                var modelName = "";

                                modelName = type == JTokenType.Array ?
                                    ((JObject)((JArray)kvp.Value).First).GetValue("model").ToString() :
                                    ((JObject)kvp.Value).GetValue("model").ToString();

                                // Console.WriteLine(stateName + " => " + modelName);
                                
                                blockStates.TryAdd(stateName, modelName);
                            }
                        }

                        // Multipart
                        if (contents.TryGetValue("multipart", out var key2))
                        {

                            foreach (JObject condition in key2)
                            {
                                var propertiesList = new List<StringBuilder>();

                                if (condition.TryGetValue("when", out var value))
                                {
                                    if (((JObject)value).TryGetValue("OR", out var conditions)) // OR statement defines multiple blockstates
                                    {
                                        var i = 0;
                                        foreach (var value2 in (JArray)conditions)
                                        {
                                            propertiesList.Add(new StringBuilder());
                                            foreach (var direction in (JObject)value2)
                                            {
                                                propertiesList[i].Append(direction.Key + "=" + direction.Value.Value<string>() + ",");
                                            }
                                            i++;
                                        }
                                    }
                                    else
                                    {
                                        propertiesList.Add(new StringBuilder());
                                        foreach (var direction in (JObject)value)
                                        {
                                            propertiesList[0].Append(direction.Key + "=" + direction.Value.Value<string>() + ",");
                                        }
                                    }
                                }
                                else
                                {
                                    propertiesList.Add(new StringBuilder());
                                }
                                
                                foreach (var properties in propertiesList)
                                {
                                    var type = condition["apply"].Type;

                                    var modelName = "";

                                    modelName = type == JTokenType.Array
                                        ? ((JObject)((JArray)condition["apply"]).First).GetValue("model").ToString()
                                        : ((JObject)condition["apply"]).GetValue("model").ToString();

                                    var stateName = properties.ToString() != ""
                                        ? name + "[" + properties.Remove(properties.Length - 1, 1) + "]"
                                        : name;
                                    
                                    blockStates.TryAdd(stateName, modelName);

                                    // Console.WriteLine(stateName + " => " + modelName);
                                }

                            }
                        }
                    }
                    
                    // model
                    if (entry.FullName.StartsWith(modId + "/models/block/", StringComparison.OrdinalIgnoreCase))
                    {
                        Assets.AdditionalModels.Add(modId + ":block/" + name, JObject.Parse(fileContents));
                        //Console.WriteLine(modId + ":block/" + name);
                    }
                }

            }

        }
        
        // -- model processing -- //

        // value integration

        foreach (var blockState in blockStates)
        {
            if (Assets.AdditionalModels.TryGetValue(blockState.Value, out JObject model))
            {
                Assets.Models.Add(blockState.Key, model);
            }
            else
            {
                Console.WriteLine("Model Not Found: " + blockState.Value + " / " + blockState.Key);
            }
        }

        foreach (var model in Assets.Models)
        {
            Assets.AdditionalModels.Remove(model.Key);
        }


        #endregion

        #region Data // for loading default data

        const string nodeName = "Root";
        TreeView.Items.Clear();
        var partsNode = new TreeView { Name = nodeName };
        partsNode.MouseDoubleClick += PartsNode_Onclick;
        TreeView.Items.Add(partsNode);
            
        
        // palette variables for processing
        var palettes = new JObject();
        var variants = new JObject();

        using (var archive = ZipFile.OpenRead(dataPath))
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    string fileContents;
                    using (var reader = new StreamReader(entry.Open()))
                    {
                        fileContents = reader.ReadToEnd();
                    }

                    var jObject = JObject.Parse(fileContents);

                    if (entry.FullName.StartsWith("lostcities/lostcities/palettes/", StringComparison.OrdinalIgnoreCase))
                    {

                        var values = new JObject();

                        foreach (var def in jObject["palette"])
                        {
                            var charValue = def["char"];

                            var value = JToken.Parse("{}");
                            var valueType = "block";


                            if (def["block"] != null)
                                value = def["block"];

                            else if (def["variant"] != null)
                            {
                                value = def["variant"];
                                valueType = "variant";
                            }

                            else if (def["blocks"] != null)
                            {
                                value = def["blocks"];
                                valueType = "blocks";
                            }

                            var paletteEntry = new JObject
                            {
                                { "type", valueType },
                                { "value", value }
                            };

                            values.Add(charValue.ToString(), paletteEntry);


                        }

                        var name = entry.Name;

                        palettes.Add(name[..^5], values);

                    }

                    if (entry.FullName.StartsWith("lostcities/lostcities/variants/", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = entry.Name;

                        variants.Add(name[..^5], jObject["blocks"]);
                    }

                    if (entry.FullName.StartsWith("lostcities/lostcities/parts/", StringComparison.OrdinalIgnoreCase))
                    {
                        var filename = entry.Name;
                        var name = filename[..^5];

                        // add file to TreeView
                        var subnode = new TreeViewItem
                        {
                            Name = name
                        };
                        subnode.MouseDoubleClick += PartsNode_Onclick;
                        subnode.Header = name;
                        partsNode.Items.Add(subnode);


                        var values = new JObject();

                        var xSize = jObject["xsize"].Value<int>();
                        var zSize = jObject["zsize"].Value<int>();

                        JToken meta = JToken.FromObject(new[] { "" });
                        JToken refpalette = JToken.FromObject("");

                        if (jObject["meta"] != null)
                            meta = jObject["meta"];

                        if (jObject["refpalette"] != null)
                            refpalette = jObject["refpalette"];


                        JArray slices = jObject["slices"].Value<JArray>();

                        values.Add("xsize", xSize);
                        values.Add("zsize", zSize);
                        values.Add("meta", meta);
                        values.Add("refpalette", refpalette);
                        values.Add("slices", slices);


                        Data.Parts.Add(name, values);

                    }
                }

            }
        }
        

        // -- palette processing -- //

        // value integration


        foreach (KeyValuePair<string, JToken> valuesPair in palettes)
        {
            var values = (JObject)valuesPair.Value;

            var valuesOut = new JObject();

            foreach (KeyValuePair<string, JToken> paletteEntryPair in values)
            {
                var paletteEntry = (JObject)paletteEntryPair.Value;

                var type = paletteEntry["type"].Value<string>();
                var value = paletteEntry["value"];

                if (type == "variant")
                {
                    value = variants[value.Value<string>()];
                }

                valuesOut.Add(paletteEntryPair.Key, value);

            }

            Data.Palettes.Add(valuesPair.Key, valuesOut);

        }

        #endregion
        
    }
    
    private void SetLayer(int value)
    {
        var maxValue = (int)Math.Round(LayerScroll.Maximum);

        if (value > maxValue)
            value = maxValue;
        else if (value < 0)
            value = 0;
        
        DisplayImage(ProgramData.Layers[value]);

        ProgramData.Layer = value;
        LayerScroll.Value = value;
        LayerNumber.Text = value.ToString();
    }

    /// <summary>
    /// Displays and image in the Editor.
    /// </summary>
    /// <param name="image">The image to display</param>
    private void DisplayImage(Image image)
    {
        Editor.Source = BitmapToSource(image);
    }
    
    /// <summary>
    /// Converts a Bitmap to a BitmapSource
    /// </summary>
    /// <param name="image">The image to convert</param>
    /// <returns>The converted image</returns>
    private static BitmapSource BitmapToSource(Image image)
    {
        using var memory = new MemoryStream();
        
        image.Save(memory, ImageFormat.Png);
        memory.Position = 0;
        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = memory;
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.EndInit();
        
        return bitmapImage;
    }
    
    /// <summary>
    /// Loads a part and renders it to the screen.
    /// </summary>
    /// <param name="partName">The name of the part</param>
    private void LoadPart(string partName)
    {
        
        var part = Data.Parts[partName].Value<JObject>();

        // Load data from part

        var refpalette = "";
        var meta = new JArray(); // ! unused model parameter !

        refpalette = part["refpalette"].Value<string>();
        meta = part["meta"].Value<JArray>();

        var xSize = part["xsize"].Value<int>();
        var zSize = part["zsize"].Value<int>();

        var slices = part["slices"].Value<JArray>();

        //

        var totalSlices = slices.Count;

        LayerScroll.Maximum = totalSlices - 1;
        LayerScroll.IsEnabled = true;
        LayerNumber.IsEnabled = true;
        
        if (refpalette != "")
        {
            LogLine("using \"" + refpalette + "\"");
        }

        var layers = new Bitmap[totalSlices];

        for (var i = 0; i < totalSlices; i++)
        {
            layers[i] = new Bitmap(xSize * 16, zSize * 16); // ! assuming 16x textures !
        }


        var iy = -1;

        foreach (JArray slice in slices)
        {
            iy++;
            var ix = 0;
            foreach (string row in slice)
            {

                ix++;
                var iz = 0;

                foreach (var blockChar in row)
                {
                    iz++;

                    var block = LookupCharDef(blockChar, refpalette);

                    var nameStrings = SplitName(block);

                    var nameSpace = nameStrings[0];
                    var name = nameStrings[1];
                    

                    var texture = new Bitmap(GetTextureFromBlockState(name, _viewDirection), 16, 16);
                    
                    var layer = layers[iy];


                    using (var g = Graphics.FromImage(layer))
                    {
                        g.DrawImage(texture, ix * 16, iz * 16);
                    }

                    layers[iy] = layer;
                    
                }
            }

        }

        ProgramData.Layers = layers;

        DisplayImage(layers[ProgramData.Layer]);
    }


    /// <summary>
    /// Splits a full name into a namespace and a name.
    /// </summary>
    /// <param name="fullName">full name, with namespace and name separated with a ':'</param>
    /// <returns>An array containing the namespace and name, in that order</returns>
    private static string[] SplitName(string fullName)
    {

        var colonPos = fullName.IndexOf(':');

        if (colonPos == -1) return new[] { "minecraft", fullName };
        var nameSpace = fullName[..colonPos];
        var name = fullName[(colonPos + 1)..];


        return new[] { nameSpace, name };
    }
    
    /// <summary>
    /// Renders a blockState to texture.
    ///<br></br><br></br>
    /// Direction Key:<br></br>
    /// 0 =  X = east<br></br>
    /// 1 =  Y = up<br></br>
    /// 2 =  Z = south<br></br>
    /// 3 = -X = west<br></br>
    /// 4 = -Y = down<br></br>
    /// 5 = -Z = north<br></br>
    
    /// </summary>
    /// <param name="blockstate">The blockstate to render</param>
    /// <param name="direction">The direction you are relative to the block</param>
    /// <returns>An image of the block from that direction</returns>
    private Bitmap GetTextureFromBlockState(string blockstate, ushort direction)
    {
        // Manual Override
        blockstate = "potted_cactus";

        // air has no model
        if (blockstate == "air")
        {
            return GetTexture("minecraft:block/air");
        }
        
        // if we do not have a model file for this block, log it
        if (!Assets.Models.ContainsKey(blockstate))
        {
            LogLine("Missing Model For Blockstate: " + blockstate);
            return GetTexture("minecraft:block/missing");
        }
        
        // Decode the model
        var decodedModel = DecodeModel(blockstate, direction);

        var isSolid = (bool)decodedModel[0];
        
        // Solid models can skip rendering it as a full model, for performance reasons
        if (isSolid)
            return GetTexture((string)decodedModel[1]);
        
        // for non-solid models:
        // get model data
        var modelElements = (List<ModelElement>)decodedModel[1];
        var textureDefinitions = (Dictionary<string, string>)decodedModel[2];
        
        // if there are no elements to render, return air
        if (modelElements.Count == 0)
            return GetTexture("air");
        
        // calculate the size of the model
        /*var maxCoord = 0;
        var minCoord = 0;*/

        var elementCount = modelElements.Count;

        var splitDirection = SplitDirection(direction);

        var axis = splitDirection[0];
        var sign = splitDirection[1];

        // sort the elements from furthest to closest
        var sortedModelElements = modelElements.ToArray().OrderBy(e => Math.Max(e.From[axis] * sign, e.To[axis] * sign));


        //var texOffsetX = new int[elementCount];
        //var texOffsetY = new int[elementCount];

        var flattenedCordsPerElement = new double[4][];
        
        flattenedCordsPerElement[0] = new double[elementCount];
        flattenedCordsPerElement[1] = new double[elementCount];
        flattenedCordsPerElement[2] = new double[elementCount];
        flattenedCordsPerElement[3] = new double[elementCount];

        var i = 0;
        foreach (var modelElement in sortedModelElements)
        {
            var flattenedCords = FlattenCoords(modelElement.From, modelElement.To, direction);

            flattenedCordsPerElement[0][i] = flattenedCords[0];
            flattenedCordsPerElement[1][i] = flattenedCords[1];
            flattenedCordsPerElement[2][i] = flattenedCords[2];
            flattenedCordsPerElement[3][i] = flattenedCords[3];

            i++;
        }
        
        // calculate the size of the resulting texture, and keep it at a minimum of 16x16
        var texSizeX = Math.Max((int)Math.Ceiling(flattenedCordsPerElement[2].Max()), 16);
        var texSizeY = Math.Max((int)Math.Ceiling(flattenedCordsPerElement[3].Max()), 16);
        
        
        Console.WriteLine("Texture Size (x, y): " + texSizeX + " x " + texSizeY);
        
        // if the model does not have anything to render, return air (OVERRIDDEN FOR DEBUGGING)
        if (texSizeX == 0 || texSizeY == 0)
            return GetTexture(modelElements[0].Tex);


        /*i = 0;
        foreach (var modelElement in sortedModelElements)
        {
            var flattenedCords = FlattenCoords(modelElement.From, modelElement.To, direction);

            //texOffsetX[i] = (int)Math.Ceiling(flattenedCords[0]);
            //texOffsetY[i] = (int)Math.Ceiling(flattenedCords[1]);
            
            // Console.WriteLine("=======TEX OFFSET X / Y=====");
            // Console.WriteLine(texOffsetX[i]);
            // Console.WriteLine(texOffsetY[i]);
            
            i++;
        }*/
        
        // create resulting texture
        var resultTexture = new Bitmap(texSizeX, texSizeY);

        i = 0;
        // draw each element onto the texture
        foreach (var modelElement in sortedModelElements)
        {
            var originalTexture = GetTexture(textureDefinitions[modelElement.Tex]);
            var texture = new Bitmap(texSizeX, texSizeY);
            
            // Add a slight "fog" to better distinguish depth
            for (var x = 0; x < texSizeX; x++)
            {
                for (var y = 0; y < texSizeY; y++)
                {
                    texture.SetPixel(x, y, FogColor);
                }
            }
            
            var flattenedCoords = FlattenCoords(modelElement.From, modelElement.To, direction);
            
            // calculate start/finish x/y coords, flipping y to go from 0=top to 0=bottom (world coords -> image coords)
            var startX  = (int)Math.Floor(flattenedCoords[0]); // 6
            var startY  = (int)Math.Floor(texSizeY - flattenedCoords[3]); // 0

            var finishX = (int)Math.Floor(flattenedCoords[2]); // 9
            var finishY = (int)Math.Floor(texSizeY - flattenedCoords[1]); // 10

            // direction to count in
            var yDec = startY > finishY;
            var xDec = startX > finishX;
            
            
            Console.WriteLine("=======START X/Y, FINISH X/Y for " + modelElement.Tex + "======");
            Console.WriteLine(startX + ", " + startY);
            Console.WriteLine(finishX + ", " + finishY);
            Console.WriteLine();

            // counters, for UV cords
            var x2 = 0;
            var y2 = 0;
            
            var uvCoords = modelElement.UV;
            
            // cut out UV from original texture and paste it on top of resulting texture
            // these for loops can change counting direction (increment or decrement). Finish values are exclusive.
            for (var x = startX; xDec ? x > finishX : x < finishX; x = xDec ? x-1 : x+1)
            {
                for (var y = startY; yDec ? y > finishY : y < finishY; y = yDec ? y-1 : y+1)
                {
                    var col = originalTexture.GetPixel(x2 + uvCoords[0], y2 + uvCoords[1]);
                    try
                    {
                        // Console.WriteLine(texOffsetX[i] + x);
                        // Console.WriteLine(texOffsetY[i] + y);
                        
                        texture.SetPixel(x, y, col);
                    }
                    catch
                    {
                        Console.WriteLine("blockstate: " + blockstate);
                        Console.WriteLine("x: " + x);
                        Console.WriteLine("y: " + y);

                        throw;
                    }

                    y2++;
                }

                y2 = 0;
                x2++;
            }
            
            using var gr2 = Graphics.FromImage(resultTexture);
            gr2.DrawImage(texture, 0, 0);

            i++;
        }

        return resultTexture;
    }
    /// <summary>
    /// Gets the texture (Bitmap) at the provided path
    /// </summary>
    /// <param name="name">The path to the texture</param>
    /// <returns>A texture (Bitmap)</returns>
    private Bitmap GetTexture(string name)
    {
        if (!name.Contains(':')) name = "minecraft:" + name;
        
        //Console.WriteLine(name);
        
        while (true)
        {
            // Command blocks are automatically replaced by air on world gen,
            // used as air that will not get replaced by other blocks.
            if (name == "minecraft:command_block")
            {
                return GetTexture("minecraft:block/air");
            }
            
            
            if (Assets.Textures.TryGetValue(name, out var texture))
                return texture;
            
            // missing texture handling
            if (!name.EndsWith('/')) LogLine("Missing Asset For: " + name);

            name = "minecraft:block/missing";
        }
    }

    /// <summary>
    /// Gets the path to the texture referenced by the provided char in the provided refpalette
    /// </summary>
    /// <param name="character">The character to look up</param>
    /// <param name="refpalette">The refpalette to look the character up in</param>
    /// <returns>The path to the texture</returns>
    private string LookupCharDef(char character, string refpalette)
    {
        while (true)
        {
            var charString = character.ToString();

            switch (character)
            {
                // filler block
                case '#' when refpalette != "bricks_standard":
                    character = '#';
                    refpalette = "bricks_standard";
                    break;
                // filler block
                case '$' when refpalette != "bricks_standard":
                    character = '#';
                    refpalette = "bricks_standard";
                    break;
                // glass block
                case 'a' when refpalette != "glass_full":
                    character = 'a';
                    refpalette = "glass_full";
                    break;
                // glass block
                case '@':
                    character = 'a';
                    refpalette = "glass_full";
                    break;
            }


            if (refpalette != "")
            {
                var palette = Data.Palettes[refpalette].Value<JObject>();

                if (palette.ContainsKey(charString))
                {
                    var value = palette[charString];

                    // does not have multiple randomly chosen blocks
                    if (!value.HasValues) return value.Value<string>();
                    
                    // has multiple randomly chosen blocks
                    var largestBlock = "";
                    var largestChance = 0;

                    foreach (JObject entry in value)
                    {
                        var chance = entry["random"].Value<int>();
                        if (chance <= largestChance) continue;
                        
                        largestChance = chance;
                        largestBlock = entry["block"].Value<string>();
                    }

                    return largestBlock;

                }

                switch (refpalette)
                {
                    case "common":
                        refpalette = "default";
                        continue;
                    case "default":
                        LogLine("missing: " + character);
                        return "";
                    default:
                        refpalette = "common";
                        continue;
                }
            }

            refpalette = "common";
        }
    }
    
    /// <summary>
    /// Gets the model for the provided blockstate, and decodes it.<br></br><br></br>
    ///
    /// Returns an Arraylist:<br></br>
    /// [0] = true if the block is solid (we can simply render the texture).<br></br>
    /// [1] = if [0] is true, contains the path to the texture to render. Otherwise it contains a List&lt;ModelData&gt;.<br></br>
    /// [2] = if [0] is true, null, otherwise it contains a Dictionary&lt;string,string&gt; of Key : Value = texture_name_in_model : path_to_texture.
    /// </summary>
    /// <param name="blockstate">The blockstate to get the model from and decode</param>
    /// <param name="direction">The direction you are relative to the block</param>
    /// <returns>The model's data</returns>
    private ArrayList DecodeModel(string blockstate, ushort direction)
    {
        var model = Assets.Models[blockstate];
        var directionString = DirectionIntToString(direction);

        var solidBlock = false;
        var solidTexture = "";

        if (model.ContainsKey("parent"))
        {
            var parent = model["parent"].Value<string>();

            if (parent is "minecraft:block/cube_all" or "minecraft:block/cube_column") // full block rendering
            {
                solidBlock = true;
                var modelTextures = (JObject)model["textures"];

                if (modelTextures.ContainsKey("all"))
                    solidTexture = modelTextures["all"].Value<string>();
                if (modelTextures.ContainsKey("side"))
                    solidTexture = modelTextures["side"].Value<string>();
            }

        }

        // solid blocks do not need fancy model interpretation. otherwise, interpret the model
        if (solidBlock) return new ArrayList { true, solidTexture, null };
        {
            var textureDefinitions = new Dictionary<string, string>();
            var modelElements = new List<ModelElement>();
            
            // elements could be in model or in parent model or in parent parent model etc.
            JArray? elements = null;


            var found = new [] { false, false };
            var currentModel = model;
            
            // get textures
            while (true)
            {
                if (currentModel.TryGetValue("elements", out var elementTokens) && !found[0])
                {
                    elements = (JArray)elementTokens;
                    found[0] = true;
                }

                // get texture definitions

                if (currentModel.TryGetValue("textures", out var modelTextures) && !found[1])
                {
                    foreach (var modelTexture in (JObject)modelTextures)
                    {
                        textureDefinitions.Add("#" + modelTexture.Key, modelTexture.Value.Value<string>());
                    }

                    found[1] = true;
                }

                if (currentModel.TryGetValue("parent", out var modelParentName))
                {
                    if (Assets.AdditionalModels.TryGetValue(modelParentName.Value<string>(), out var modelParent))
                        currentModel = modelParent;
                }
                else
                    break; //quit

                if (found[0] && found[1])
                    break; // exit with results
            }
            
            // get cube bounds / UV
            if (elements != null)
            {

                // Load model elements
                foreach (var element in elements)
                {
                    var modelElement = new ModelElement();

                    // Cube bounds (to/from)
                    var fromTokens = new JToken[3];
                    var toTokens = new JToken[3];
                    ((JArray)element["from"]).CopyTo(fromTokens, 0);
                    ((JArray)element["to"]).CopyTo(toTokens, 0);
                    for (var i = 0; i < 3; i++)
                    {
                        modelElement.From[i] = fromTokens[i].Value<double>();
                        modelElement.To[i] = toTokens[i].Value<double>();
                    }

                    // Cube UV
                    if (((JObject)element["faces"]).TryGetValue(directionString, out var face))
                    {

                        modelElement.Tex = face["texture"].Value<string>();

                        var uvTokens = new JToken[4];

                        if (!((JObject)face).ContainsKey("uv")) // if the face does not specify a texture UV, use the whole texture, masked by the cube bounds
                        {
                            var flattenedCords = FlattenCoords(modelElement.From, modelElement.To, direction);
                            
                            // get the UV
                            ((JObject)face).Add("uv", new JArray { flattenedCords[0], flattenedCords[1], flattenedCords[2], flattenedCords[3] });
                        }


                        ((JArray)face["uv"]).CopyTo(uvTokens, 0);
                        for (var i = 0; i < 4; i++)
                            modelElement.UV[i] = uvTokens[i].Value<int>();


                        modelElements.Add(modelElement);
                    }

                }
            }
            return new ArrayList {false, modelElements, textureDefinitions};
        }

    }
    
    
    /// <summary>
    /// Converts 2 opposing 3D Coordinates (To and From for a rectangular prism) into 2 opposing corner coordinates (3D) of a 2D rectangle in this order: <br></br>
    /// [0] = Left (Min X)<br></br>
    /// [3] = Bottom (Min Y)<br></br>
    /// [2] = Right (Max X)<br></br>
    /// [1] = Top (Max Y)<br></br>
    /// </summary>
    /// <param name="c1">The first 3D Coordinate</param>
    /// <param name="c2">The second, opposing, 3D Coordinate</param>
    /// <param name="direction">The direction we are, relative to the object (negated facing direction)</param>
    /// <returns>The edge positions of the resulting square</returns>
    private static double[] FlattenCoords(IReadOnlyList<double> c1, IReadOnlyList<double> c2, ushort direction)
    {
        // get position of the 4 faces which make up the 4 edges of the rectangle
        var projectionPlane = ProjectionPlanes[direction];
        
        // get axis part of the direction
        var horizontalAxis = SplitDirection(projectionPlane[0])[0];
        var verticalAxis = SplitDirection(projectionPlane[1])[0];
        
        var x1 = c1[horizontalAxis];
        var x2 = c2[horizontalAxis];
        var y1 = c1[verticalAxis];
        var y2 = c2[verticalAxis];

        var right = Math.Max(x1, x2);
        var left = Math.Min(x1, x2);

        var top = Math.Max(y1, y2);
        var bottom = Math.Min(y1, y2);
        
        return new[]
        {
            left,
            bottom,
            right,
            top
        };
    }
    
    /// <summary>
    /// Converts a ushort (range 0-5) the name of one of the 6 the cardinal directions.
    /// </summary>
    /// <param name="direction"></param>
    /// <returns>A string containing the name of the cardinal direction referenced</returns>
    private static string DirectionIntToString(ushort direction)
    {
        return direction switch
        {
            0 => "east",
            1 => "up",
            2 => "south",
            3 => "west",
            4 => "down",
            5 => "north",
            _ => throw new Exception("Invalid Direction (ushort)")
        };
    }
    
    /// <summary>
    /// Converts the name of one of the 6 the cardinal directions into a ushort (range 0-5) for more compact storage.
    /// </summary>
    /// <param name="direction">A string containing the direction</param>
    /// <returns>The direction as a ushort (0-5)</returns>
    private static ushort DirectionStringToInt(string direction)
    {
        return direction switch
        {
            "east" => 0,
            "up" => 1,
            "south" => 2,
            "west" => 3,
            "down" => 4,
            "north" => 5,
            _ => throw new Exception("Invalid Direction (string)")
        };
    }
    
    /// <summary>
    /// Splits a direction (x,y,z +\-) into an axis (x,y,z => 0-2) and a sign (the direction on that axis => -1/+1)
    /// </summary>
    /// <param name="direction">A ushort (0-5 range) that specifies a direction</param>
    /// <returns>An array containing the axis and sign, in that order</returns>
    private static short[] SplitDirection(ushort direction)
    {
        if (direction > 5)
            throw new Exception("Invalid Direction (ushort)");
        
        var axis = (short)(direction > 2 ? direction - 3 : direction);
        var sign = (short)(direction < 3 ? 1 : -1);

        return new[] { axis, sign };
    }

    /// <summary>
    /// Re-renders the current part
    /// </summary>
    private void ReRenderCurrentPart()
    {
        LoadPart(_currentPartName);
    }

    /// <summary>
    /// Logs a message (on a new line) to the in-program debug "console"
    /// </summary>
    /// <param name="s">The message to log</param>
    private void LogLine(string s)
    {
        Log.Text = Log.Text + s + Environment.NewLine;

    }
    /// <summary>
    /// A struct to hold all the parameters of an element (a textured rectangular prism) in a model
    /// </summary>
    private struct ModelElement
    {
        /// <summary>
        /// Starting coordinate for the rectangular prism
        /// </summary>
        public readonly double[] From = new double[3];
        
        /// <summary>
        /// Finishing coordinate for the rectangular prism
        /// </summary>
        public readonly double[] To = new double[3];
        
        /// <summary>
        /// The path to the texture
        /// </summary>
        public string Tex = "";
        
        /// <summary>
        /// UV coordinates for the texture<br></br>
        /// [0] = Start X<br></br>
        /// [1] = Start Y<br></br>
        /// [2] = Width<br></br>
        /// [3] = Height
        /// </summary>
        public int[] UV = new int[4];

        public ModelElement()
        {
        }
    }
    
    /// <summary>
    /// Direction Chooser handler
    /// </summary>
    private void DirectionChooser_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // set new view direction
        _viewDirection = DirectionStringToInt(((string)((ComboBoxItem)DirectionChooser.SelectedValue).Content).ToLower());
        
        // reload the part to apply changes
        ReRenderCurrentPart();
    }
    
    /// <summary>
    /// Layer Scrollbar handler
    /// </summary>
    private void LayerScrollChange(object sender, EventArgs e)
    {
        SetLayer((int)Math.Round(LayerScroll.Value));
    }
    /// <summary>
    /// Layer Manual Setter handler
    /// </summary>
    private void LayerNumber_TextChanged(object sender, EventArgs e)
    {
        try
        {
            var value = int.Parse(LayerNumber.Text);

            SetLayer(value);
        }
        catch { }

    }
    
    /// <summary>
    /// Scrolling on Editor changes layer number
    /// </summary>
    private void Editor_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetLayer(e.Delta < 0 ? ProgramData.Layer - 1 : ProgramData.Layer + 1);
    }
    
    /// <summary>
    /// Part Selection handler
    /// </summary>
    private void PartsNode_Onclick(object sender, MouseButtonEventArgs e)
    {
        string nodeName;

        try
        {
            nodeName = ((TreeViewItem)sender).Name;
        }
        catch
        {
            nodeName = ((TreeView)sender).Name;
        }
        
        
        if (nodeName == "Root")
            return;

        _currentPartName = nodeName;
        
        LoadPart(nodeName);
    }

}
/// <summary>
/// Loaded Lost Cities Data Class
/// </summary>
internal static class Data
{
    public static readonly JObject Palettes = new();
    public static readonly JObject Parts = new();
}

/// <summary>
/// Assets Class<br></br><br></br>
/// Dictionaries: <br></br>
/// Textures - path_to_texture => texture<br></br>
/// Models - blockstate => model_json<br></br>
/// AdditionalModels - name_of_model => model_json<br></br>
/// </summary>
internal static class Assets
{
    public static readonly Dictionary<string, Bitmap> Textures = new();             // path_to_texture => texture          | minecraft/textures/block/oak_log => (stone bitmap)
    public static readonly Dictionary<string, JObject> Models = new();              // name_of_blockstate => model_json    | oak_log[axis=x] : (oak_log model for x axis facing direction)
    public static readonly Dictionary<string, JObject> AdditionalModels = new();    // name_of_model => model_json         | minecraft:block/cube/all : (block model for x axis facing direction)
}
/// <summary>
/// Layer - Current layer index<br></br>
/// Layers - All the layers (Bitmap) for the currently loaded part.
/// </summary>
internal static class ProgramData
{
    public static int Layer;
    public static Bitmap[] Layers = Array.Empty<Bitmap>();
}
