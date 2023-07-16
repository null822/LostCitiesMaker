using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Windows.Controls;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
        Dictionary<string, JObject> models = new();


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

                    Assets.Textures.Add(image);
                    Assets.TextureIndex.Add(path, index);

                    index++;
                }

                if (entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var name = filename[..^5];

                    string fileContents;
                    using (var reader = new StreamReader(entry.Open()))
                    {
                        fileContents = reader.ReadToEnd();
                    }

                    // blockstate
                    if (entry.FullName.StartsWith("minecraft/blockstates/", StringComparison.OrdinalIgnoreCase))
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

                                var modelNameBuilder = new StringBuilder(modelName);
                                modelNameBuilder.Replace("minecraft:block/", "");
                                    
                                blockStates.TryAdd(stateName, modelNameBuilder.ToString());
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

                                    var modelNameBuilder = new StringBuilder(modelName);
                                    modelNameBuilder.Replace("minecraft:block/", "");
                                    
                                    blockStates.TryAdd(stateName, modelNameBuilder.ToString());

                                    // Console.WriteLine(stateName + " => " + modelName);
                                }

                            }
                        }
                    }
                    
                    // model
                    if (entry.FullName.StartsWith("minecraft/models/block/", StringComparison.OrdinalIgnoreCase))
                    {
                        models.Add(name, JObject.Parse(fileContents));
                    }
                }

            }

        }
        
        // -- model processing -- //

        // value integration

        foreach (var blockState in blockStates)
        {
            if (models.ContainsKey(blockState.Value))
            {
                Assets.Models.Add(blockState.Key, models[blockState.Value]);
            }
            else
            {
                Console.WriteLine("Model Not Found: " + blockState.Value);
            }
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
                    

                    var texture = new Bitmap(GetTexture(nameSpace + "/textures/block/" + name), 16, 16);
                    
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

    private Bitmap GetTexture(string name)
    {
        while (true)
        {
            // Command blocks are automatically replaced by air on world gen,
            // used as air that will not get replaced by other blocks.
            if (name == "minecraft/textures/block/command_block")
            {
                GetTexture("minecraft/textures/block/air");
            }

            if (Assets.TextureIndex.TryGetValue(name, out var value))
            {
                var index = value.Value<int>();
                return Assets.Textures[index];
            }

            if (!name.EndsWith('/')) LogLine("missing asset: " + name);

            name = "minecraft/textures/missing";
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
}

internal static class Data
{
    //public static List<string> Blocks = new();
    public static readonly JObject Palettes = new();
    public static readonly JObject Parts = new();
}

internal static class Assets
{
    public static readonly List<Bitmap> Textures = new();
    public static readonly JObject TextureIndex = new();

    public static readonly Dictionary<string, JObject> Models = new();

}

internal static class ProgramData
{
    public static int Layer;
    public static Bitmap[] Layers = Array.Empty<Bitmap>();
}
