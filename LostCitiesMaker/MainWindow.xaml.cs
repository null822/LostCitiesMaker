using System;
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
using Image = System.Drawing.Image;

namespace LostCitiesMaker;

public partial class MainWindow
{
    private const int Scale = 1;

    
    public MainWindow()
    {
        InitializeComponent();
        
        RenderOptions.SetBitmapScalingMode(Editor, BitmapScalingMode.NearestNeighbor);

        LoadData();
    }
    
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
        partsNode.MouseDoubleClick += LoadPart;
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
                        subnode.MouseDoubleClick += LoadPart;
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


    private void LayerScrollChange(object sender, EventArgs e)
    {
        var value = (int)Math.Round(LayerScroll.Value);

        SetLayer(value);
    }
    
    private void LayerNumber_TextChanged(object sender, EventArgs e)
    {
        try
        {
            var value = int.Parse(LayerNumber.Text);

            SetLayer(value);
        }
        catch { }

    }
    
    private void Editor_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetLayer(e.Delta < 0 ? ProgramData.Layer - 1 : ProgramData.Layer + 1);
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

    private void DisplayImage(Image image)
    {
        Editor.Source = BitmapToSource(image);
    }
    
    private static Bitmap ResizeImage(Image image, int width, int height)
    {
        var destRect = new Rectangle(0, 0, width, height);
        var destImage = new Bitmap(width, height);

        destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

        using var graphics = Graphics.FromImage(destImage);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using var wrapMode = new ImageAttributes();
        wrapMode.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(image, destRect, 0, 0, image.Width,image.Height, GraphicsUnit.Pixel, wrapMode);

        return destImage;
    }
    
    
    
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


    private void LoadPart(object sender, MouseButtonEventArgs e)
    {
        var nodeName = "Root";

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


        var part = Data.Parts[nodeName].Value<JObject>();

        // Load data from part

        var refpalette = "";
        var meta = new JArray();

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

                    var nameStrings = GetNameSpace(block);

                    var nameSpace = nameStrings[0];
                    var name = nameStrings[1];
                    

                    var texture = new Bitmap(GetTextureFromBlockState(name, 5), 16, 16);
                    
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

        DisplayImage(layers[0]);
    }



    private static string[] GetNameSpace(string fullName)
    {

        var colonPos = fullName.IndexOf(':');

        if (colonPos == -1) return new[] { "minecraft", fullName };
        var nameSpace = fullName[..colonPos];
        var name = fullName[(colonPos + 1)..];


        return new[] { nameSpace, name };
    }
    
    /**
     * Converts blockState to Texture.
     * 
     * direction:
     * 0 :  X : east
     * 1 :  Y : up
     * 2 :  Z : south
     * 3 : -X : west
     * 4 : -Y : down
     * 5 : -Z : north
     */
    private Bitmap GetTextureFromBlockState(string blockstate, ushort direction)
    {
        var directionString = direction switch
        {
            0 => "east",
            1 => "up",
            2 => "south",
            3 => "west",
            4 => "down",
            5 => "north",
            _ => ""
        };

        if (blockstate == "air")
        {
            return GetTexture("minecraft:block/air");
        }
        
        if (!Assets.Models.ContainsKey(blockstate))
        {
            return GetTexture(blockstate);
        }
        
        var model = Assets.Models[blockstate];

        if (model.ContainsKey("parent"))
        {
            var parent = model["parent"].Value<string>();

            if (parent is "minecraft:block/cube_all" or "minecraft:block/cube_column")
            {
                var modelTextures = (JObject)model["textures"];

                if (modelTextures.ContainsKey("all"))
                    return GetTexture(modelTextures["all"].Value<string>());
                if (modelTextures.ContainsKey("side"))
                    return GetTexture(modelTextures["side"].Value<string>());
            }

        }

        //var parentModel = Assets.AdditionalModels[parent];
        

        // elements could be in model or in parent model or in parent parent model etc.
            JArray? elements = null;
            
            var textureDefinitions = new Dictionary<string, string>();

            var found = new bool[] { false, false };
            
            var currentModel = model;
            while (true)
            {
                if (currentModel.TryGetValue("elements", out var elementTokens) && !found[0])
                {
                    elements = (JArray)elementTokens;
                    found[0] = true;
                }
                
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

            var modelElements = new List<ModelElement>();

            if (elements != null)
            {
                Console.WriteLine("found. " + blockstate);

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
                        modelElement.From[i] = fromTokens[i].Value<int>();
                        modelElement.To[i] = toTokens[i].Value<int>();
                    }
                    
                    // Faces
                    if (((JObject)element["faces"]).TryGetValue(directionString, out var face))
                    {
                        // Texture UV
                        var uvTokens = new JToken[4];
                        ((JArray)face["uv"]).CopyTo(uvTokens, 0);
                        for (var i = 0; i < 4; i++)
                            modelElement.UV[i] = uvTokens[i].Value<int>();

                        modelElement.Tex = face["texture"].Value<string>();
                        
                        modelElements.Add(modelElement);
                    }
                    
                }
            }

            var sign = (short)(direction < 3 ? 1 : -1);
            var axis = (short)(direction > 2 ? direction - 3 : direction);
            
            //Console.WriteLine(sign + ", " + axis);
            
            var sortedModelElements = modelElements.ToArray().OrderBy(e => Math.Max(e.From[axis] * sign, e.To[axis] * sign));

            var resultTexture = new Bitmap(16, 16); // ! assuming 16x textures !
            foreach (var modelElement in sortedModelElements)
            {
                var width = Math.Abs(modelElement.UV[0] - modelElement.UV[2]);
                var height = Math.Abs(modelElement.UV[1] - modelElement.UV[3]);
                
                var originalTexture = GetTexture(textureDefinitions[modelElement.Tex]);
                var texture = new Bitmap(width, height);

                
                for (var x = 0; x < width; x++)
                {
                    for (var y = 0; y < height; y++)
                    {
                        texture.SetPixel(x, y, originalTexture.GetPixel(x + modelElement.UV[0], y + modelElement.UV[1]));
                    }
                }
                
                texture.Save("temp.png");
                

                using var gr2 = Graphics.FromImage(resultTexture);
                gr2.DrawImage(texture, modelElement.From[0], modelElement.From[1]);
            }

            return resultTexture;
        

        // non-cube: "uv": [ x, y, w, h ]
        
        return GetTexture(blockstate);
    }

    private Bitmap GetTexture(string name)
    {
        if (!name.Contains(':')) name = "minecraft:" + name;
        
        while (true)
        {
            // Command blocks are automatically replaced by air on world gen,
            // used as air that will not get replaced by other blocks.
            if (name == "minecraft:block/command_block")
            {
                GetTexture("minecraft:block/air");
            }
            

            if (Assets.Textures.TryGetValue(name, out var texture))
                return texture;
            
            // missing texture handling
            if (!name.EndsWith('/')) LogLine("Missing Asset For: " + name);

            name = "minecraft:block/missing";
        }
    }


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


    private void LogLine(string s)
    {
        Log.Text = Log.Text + s + Environment.NewLine;

    }

    private struct ModelElement
    {
        public int[] From = new int[3];
        public int[] To = new int[3];

        public string Tex = "";
        
        public int[] UV = new int[4];

        public ModelElement()
        {
        }
    }
}

internal static class Data
{
    //public static List<string> Blocks = new();
    public static readonly JObject Palettes = new();
    public static readonly JObject Parts = new();
}

internal static class Assets
{
    public static readonly Dictionary<string, Bitmap> Textures = new(); // path_to_texture : texture      | minecraft/textures/block/oak_log : (stone bitmap)
    public static readonly Dictionary<string, JObject> Models = new(); // name_of_blockstate : model_json | oak_log[axis=x] : (oak_log model (axis = x))
    public static readonly Dictionary<string, JObject> AdditionalModels = new(); // name_of_model : model_json | minecraft:block/cube/all : (block model (axis = x))
}

internal static class ProgramData
{
    public static int Layer;
    public static Bitmap[] Layers = Array.Empty<Bitmap>();
}
