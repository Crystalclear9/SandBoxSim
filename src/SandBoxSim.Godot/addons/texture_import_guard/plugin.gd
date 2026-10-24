@tool
extends EditorPlugin
var failed=false

func _enter_tree():
    if "--reimport-textures" in OS.get_cmdline_user_args():
        run.call_deferred()

func run():
    var filesystem=EditorInterface.get_resource_filesystem()
    while filesystem.is_scanning():
        await get_tree().process_frame
    filesystem.scan()
    while filesystem.is_scanning():
        await get_tree().process_frame
    var files=PackedStringArray()
    collect("res://assets",files)
    if failed:
        get_tree().quit(2)
        return
    if files.is_empty():
        push_error("No texture mip chains found")
        get_tree().quit(2)
        return
    filesystem.reimport_files(files)
    var failures=0
    for path in files:
        var texture=ResourceLoader.load(path,"",ResourceLoader.CACHE_MODE_IGNORE)
        if not texture is Texture2D or not texture.get_image().has_mipmaps():
            push_error("Imported mip chain still missing: "+path)
            failures+=1
    if failures==0:
        print("TEXTURE_REIMPORT_PASS: ",files.size()," actual mip chains verified")
    get_tree().quit(0 if failures==0 else 2)

func collect(folder:String,files:PackedStringArray):
    var directory=DirAccess.open(folder)
    if directory==null:
        push_error("Cannot inspect asset directory: "+folder)
        failed=true
        return
    for child in directory.get_directories():
        collect(folder.path_join(child),files)
    for name in directory.get_files():
        if not name.ends_with(".import"):
            continue
        var config=ConfigFile.new()
        var source=folder.path_join(name.trim_suffix(".import"))
        if config.load(folder.path_join(name))!=OK or config.get_value("remap","importer","")!="texture":
            continue
        var required=folder.begins_with("res://assets/models/") or folder.begins_with("res://assets/materials/")
        if required and not config.get_value("params","mipmaps/generate",false):
            config.set_value("params","mipmaps/generate",true)
            if config.save(folder.path_join(name))!=OK:
                push_error("Cannot save mip import settings: "+source)
                failed=true
                return
        if config.get_value("params","mipmaps/generate",false):
            files.append(source)
