using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using QuestMapStudio.Core;

int passed = 0;
void Check(bool v,string n) { if (!v) throw new Exception("FAIL " + n); Console.WriteLine("PASS " + n); passed++; }
async Task Reject(Func<Task> f,string n) { try { await f(); } catch { Check(true,n); return; } throw new Exception("Expected rejection: " + n); }
byte[] B(string x) => Encoding.UTF8.GetBytes(x);
string v2 = "{\"_version\":\"2.1.0\",\"_songName\":\"Title\",\"_songAuthorName\":\"Artist\",\"_levelAuthorName\":\"Mapper\",\"_difficultyBeatmapSets\":[{\"_beatmapCharacteristicName\":\"Standard\",\"_difficultyBeatmaps\":[{\"_difficulty\":\"Expert\",\"_beatmapFilename\":\"Expert.dat\",\"_customData\":{\"_requirements\":[\"Noodle Extensions\"],\"_suggestions\":[\"Chroma\"]}}]}]}";
var map = MapEntry.Parse("/sdcard/maps/A","Info.dat",B(v2));
Check(map.Title == "Title" && map.Artist == "Artist" && map.Mapper == "Mapper","metadata separates musician and mapper");
Check(map.RequiredText == "Noodle Extensions" && map.SuggestedText == "Chroma","required mods separate from suggestions");
Check(map.Difficulties.Single().Name == "Expert","per-difficulty metadata");
var files = new Dictionary<string,byte[]> { ["Info.dat"] = B(v2),["Expert.dat"] = B("{ \"notes\": [] }") };
var hash = await map.ComputeHash(p => Task.FromResult(files[p]));
Check(hash == Convert.ToHexString(SHA1.HashData(files["Info.dat"].Concat(files["Expert.dat"]).ToArray())),"v2 SHA1 exact ordered raw bytes");
var bom = new byte[] {239,187,191}.Concat(B(v2)).ToArray(); var bm = MapEntry.Parse("x","Info.dat",bom);
Check(bm.RawInfo == Convert.ToBase64String(bom),"BOM retained for hash");
var v4 = """
{"version":"4.0.0","song":{"title":"V4","author":"Singer"},"audio":{"audioDataFilename":"AudioData.dat"},"difficultyBeatmaps":[{"characteristic":"Standard","difficulty":"Easy","beatmapDataFilename":"Easy.dat","lightshowDataFilename":"Lights.dat","beatmapAuthors":{"mappers":["Alice"]},"customData":{"requirements":["Chroma"]}},{"characteristic":"Standard","difficulty":"Expert","beatmapDataFilename":"Expert.dat","lightshowDataFilename":"Lights.dat","beatmapAuthors":{"mappers":["Bob"]}}]}
""";
var m4 = MapEntry.Parse("x","info.dat",B(v4));
Check(m4.Mapper == "Alice, Bob" && m4.RequiredText == "Chroma","v4 authors and requirements");
Check(m4.HashFiles.SequenceEqual(new[] {"info.dat","AudioData.dat","Easy.dat","Lights.dat","Expert.dat","Lights.dat"}),"v4 audio data + ordered charts, repeated shared lightshow");
var f4 = m4.HashFiles.Distinct().ToDictionary(x=>x,x=>x=="info.dat" ? B(v4) : B(x));
Check(await m4.ComputeHash(p=>Task.FromResult(f4[p])) == Convert.ToHexString(SHA1.HashData(m4.HashFiles.SelectMany(p=>f4[p]).ToArray())),"v4 SHA1 golden concatenation");
foreach (var unsafePath in new[] {"../Expert.dat","/Expert.dat","a\\b.dat","a//b","a/./b","C:abc","a\nb"}) await Reject(()=>{MapEntry.ValidateRelative(unsafePath);return Task.CompletedTask;},"unsafe Info reference " + unsafePath.Replace('\n',' '));
await Reject(()=>Task.FromResult(MapEntry.Parse("x","Info.dat",B("{}"))),"unknown Info version rejected");
var doc = PlaylistDocument.Parse("""
{"playlistTitle":"Original","customData":{"foreign":123},"mystery":true,"songs":[{"hash":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","songName":"A","difficulties":[{"name":"Expert","characteristic":"Standard"}],"foreign":7},{"levelid":"OST_Example","songName":"Builtin"}]}
""");
doc.Title = "Changed"; doc.Author = "Author"; doc.Move(0,1);
var round = PlaylistDocument.Parse(Encoding.UTF8.GetString(doc.Bytes()));
Check(round.Root["customData"]!["foreign"]!.GetValue<int>() == 123 && round.Root["mystery"]!.GetValue<bool>(),"unknown root fields preserved");
Check(round.Songs[1]!["foreign"]!.GetValue<int>() == 7 && round.Songs[1]!["difficulties"]!.AsArray().Count == 1,"unknown entry fields and difficulties preserved on reorder");
Check(round.Songs[0]!["levelid"]!.GetValue<string>() == "OST_Example","builtin entries retained");
Check(round.Add(map,hash) && !round.Add(map,hash.ToLowerInvariant()),"game-hash deduplication case insensitive");
await Reject(()=>{ round.Add(map,new string('Z',40)); return Task.CompletedTask; },"reject invalid playlist hash");
round.Songs.RemoveAt(0); Check(round.Songs.Count == 2,"playlist entry removal");
await Reject(()=>{round.SetCover(B("not an image"),"");return Task.CompletedTask;},"cover signature checked");
var png = new byte[] {137,80,78,71,13,10,26,10,0}; round.SetCover(png,"");
Check(MapEntry.S(round.Root,"imageString") == Convert.ToBase64String(png) && MapEntry.S(round.Root,"image").StartsWith("data:image/png;base64,"),"cover for Quest and PC formats");
var fake = new FakeDevice(); var store = new PlaylistStore(fake); var fresh = PlaylistDocument.New(); fresh.Title = "Fresh";
await store.Save(fresh,default);
Check(fresh.RemotePath!.StartsWith(QuestRepository.Playlists+"/QMS-") && fresh.RemoteDigest == QuestRepository.Digest(fresh.Bytes()),"new playlist verified and published to actual PlaylistManager path");
Check(fake.Files[fresh.RemotePath].SequenceEqual(fresh.Bytes()) && !fake.Files.Keys.Any(x=>x.EndsWith(".tmp")),"atomic publish cleaned staging");
byte[] previous = fake.Files[fresh.RemotePath]; fresh.Title = "Update"; await store.Save(fresh,default);
Check(fake.Files.Where(x=>x.Key.Contains("Backups/")).Any(x=>x.Value.SequenceEqual(previous)),"old playlist backed up before overwrite");
var before = fake.Files[fresh.RemotePath]; fake.Files[fresh.RemotePath] = B("external change");
await Reject(()=>store.Save(fresh,default),"external remote edit blocks overwrite");
Check(Encoding.UTF8.GetString(fake.Files[fresh.RemotePath]) == "external change","conflict does not alter external file");
fake.Files[fresh.RemotePath] = before; fake.CorruptUploads = true; fresh.Title = "Changed after upload";
await Reject(()=>store.Save(fresh,default),"corrupt upload blocks publish");
Check(fake.Files[fresh.RemotePath].SequenceEqual(before),"corrupt upload leaves old playlist intact"); fake.CorruptUploads=false;
using (var cts = new CancellationTokenSource()) { cts.Cancel(); await Reject(()=>store.Save(fresh,cts.Token),"cancelled save does not publish"); }
fake.GameOpen = true; await Reject(()=>store.Save(fresh,default),"live game blocks playlist write"); fake.GameOpen=false;
var deletedPath = fresh.RemotePath!; var backup = await store.Delete(fresh);
Check(fake.Files.ContainsKey(backup) && !fake.Files.ContainsKey(deletedPath) && fresh.RemotePath == null,"delete retains verified backup and clears remote identity");
Check(!fake.Commands.Any(x=>x.Contains("CustomLevels")),"playlist operations never touch map directories");
var temp = Path.Combine(Path.GetTempPath(),"qms-data-test-"+Guid.NewGuid().ToString("N"));
try
{
    var local = new LocalData(temp); local.SaveMaps("quest-one",new(){map}); local.SaveDraft("quest-one",round);
    Check(local.LoadMaps("quest-one").Single().GameHash == map.GameHash,"catalogue serialized");
    Check(local.LoadMaps("quest-two").Count==0 && local.LoadDrafts("quest-two").Count==0,"device caches isolated by serial");
    var restored = local.LoadDrafts("quest-one").Single(); Check(restored.Title == round.Title && restored.RemoteDigest == round.RemoteDigest,"offline draft preserves payload and conflict token");
    local.DeleteDraft("quest-one",restored); Check(local.LoadDrafts("quest-one").Count==0 && Directory.GetFiles(Path.Combine(temp,"Backups")).Length==1,"local draft deletion backed up");
}
finally { Directory.Delete(temp,true); }
if (!OperatingSystem.IsWindows())
{
    string adbTemp = Path.Combine(Path.GetTempPath(),"qms-adb-test-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(adbTemp);
    try
    {
        string script = Path.Combine(adbTemp,"adb");
        await File.WriteAllTextAsync(script,"#!/usr/bin/python3\nimport sys,subprocess\na=sys.argv[1:]\nif a[:1]==['-s']: a=a[2:]\nif a[0] in ['shell','exec-out']: sys.exit(subprocess.call(a[-1],shell=True))\nif a[0]=='get-state': print('device')\n");
        File.SetUnixFileMode(script,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        string folder = Path.Combine(adbTemp,"曲 ' special"); Directory.CreateDirectory(folder); string path=Path.Combine(folder,"Info.dat"); await File.WriteAllBytesAsync(path,B(v2));
        var adb = new AdbClient(script,"quest-serial");
        Check((await adb.Read(path)).SequenceEqual(B(v2)),"ADB shell quoting handles Unicode and apostrophe");
        Check((await adb.Find(folder,"\\( -name Info.dat -o -name info.dat \\)")).Single()==path,"ADB find uses NUL framing and safe shell quoting");
        await Reject(()=>adb.Read(path,10),"ADB bounded binary response");
        await adb.CheckConnection(); Check(true,"ADB device binding and connection state");
        var repo=new QuestRepository(adb);
        string chart=Path.Combine(folder,"Expert.dat"); await File.WriteAllBytesAsync(chart,files["Expert.dat"]);
        var remoteMap=MapEntry.Parse(folder,"Info.dat",B(v2));
        // Desktop /bin/sh has no pipefail; use bash in this transport fixture to model Android mksh.
        string scriptText=await File.ReadAllTextAsync(script); scriptText=scriptText.Replace("shell=True)","shell=True,executable='/bin/bash')"); await File.WriteAllTextAsync(script,scriptText);
        Check(await repo.ResolveHash(remoteMap)==hash,"remote hash command concatenates original files");
        File.Delete(chart); await Reject(()=>repo.ResolveHash(remoteMap),"missing remote chart does not yield a usable partial hash");
    }
    finally { Directory.Delete(adbTemp,true); }
}
Console.WriteLine($"All {passed} core tests passed.");

sealed class FakeDevice : IPlaylistDevice
{
    public Dictionary<string,byte[]> Files = new(); public List<string> Commands = new(); public bool CorruptUploads; public bool GameOpen;
    public Task Check() { if (GameOpen) throw new IOException("Game open"); return Task.CompletedTask; }
    public Task<string?> Digest(string path) => Task.FromResult(Files.TryGetValue(path,out var b) ? QuestRepository.Digest(b) : null);
    public async Task Upload(string local,string remote) => Files[remote] = CorruptUploads ? new byte[] {1,2,3} : await File.ReadAllBytesAsync(local);
    public Task Shell(string command)
    {
        Commands.Add(command);
        // All generated paths are single-quoted. Simulate filesystem primitives, not a real shell.
        var tokens = System.Text.RegularExpressions.Regex.Matches(command,"'([^']*)'").Select(x=>x.Groups[1].Value).ToArray();
        if (command.StartsWith("mv -f")) { Files[tokens[1]] = Files[tokens[0]]; Files.Remove(tokens[0]); }
        else if (command.StartsWith("cp ") || command.Contains(" && cp ")) Files[tokens[^1]] = Files[tokens[^2]].ToArray();
        else if (command.StartsWith("rm ")) Files.Remove(tokens[0]);
        return Task.CompletedTask;
    }
}
