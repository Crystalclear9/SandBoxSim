extends SceneTree
func _initialize():call_deferred("sample")
func frames(count:int):
    for i in range(count):await process_frame
func sample():
    var app=load("res://Main.tscn").instantiate();root.add_child(app)
    await frames(15)
    var functions="";var far_code=""
    for node in app.find_children("*","MeshInstance3D",true,false):
        if node.mesh==null:continue
        for index in range(node.mesh.get_surface_count()):
            var material=node.get_surface_override_material(index)
            if material==null:material=node.mesh.surface_get_material(index)
            if not material is ShaderMaterial:continue
            var code=material.shader.code
            if code.contains("vec3 skin_colour") and code.contains("uniform vec4 tone"):
                var start=code.find("vec3 skin_linear")
                functions=code.substr(start,code.find("uniform vec4 tone")-start)
            if code.contains("ALBEDO=OUTPUT_IS_SRGB?COLOR.rgb"):far_code=code
    if functions=="" or far_code=="":
        push_error("Production skin and LOD shader not found");quit(1);return
    var viewport=SubViewport.new();viewport.size=Vector2i(64,64);viewport.own_world_3d=true
    viewport.render_target_update_mode=SubViewport.UPDATE_ALWAYS;root.add_child(viewport)
    var environment=WorldEnvironment.new();var settings=Environment.new()
    settings.background_mode=Environment.BG_COLOR;settings.background_color=Color.BLACK
    settings.tonemap_mode=Environment.TONE_MAPPER_LINEAR;environment.environment=settings;viewport.add_child(environment)
    var camera=Camera3D.new();camera.projection=Camera3D.PROJECTION_ORTHOGONAL;camera.size=2
    camera.position=Vector3(0,0,2);viewport.add_child(camera);camera.look_at(Vector3.ZERO)
    var quad=MeshInstance3D.new();quad.mesh=QuadMesh.new();quad.mesh.size=Vector2(2,2);viewport.add_child(quad)
    var shader=Shader.new()
    shader.code="shader_type spatial;render_mode unshaded,cull_disabled;"+functions+"uniform sampler2D probe:source_color,filter_nearest;uniform vec4 tone:source_color;uniform vec4 reference_tone:source_color;uniform float gain;void fragment(){ALBEDO=skin_colour(texture(probe,UV).rgb,tone.rgb,reference_tone.rgb,gain);}"
    var material=ShaderMaterial.new();material.shader=shader;quad.material_override=material
    var image=Image.create(8,8,false,Image.FORMAT_RGBA8);image.fill(Color(.7,.5,.35))
    var texel=image.get_pixel(0,0);var tint=Color(.55,.44,.36);var reference=Color(.65,.5,.4)
    material.set_shader_parameter("probe",ImageTexture.create_from_image(image))
    material.set_shader_parameter("tone",tint);material.set_shader_parameter("reference_tone",reference)
    var maximum=0.0
    for gain in [1.0,1.299263,.918634]:
        material.set_shader_parameter("gain",gain)
        await frames(4);await RenderingServer.frame_post_draw
        var observed=viewport.get_texture().get_image().get_pixel(32,32)
        var a=texel.srgb_to_linear();var t=tint.srgb_to_linear();var r=reference.srgb_to_linear()
        var expected=Color(a.r*.88*gain*t.r/r.r,a.g*.88*gain*t.g/r.g,a.b*.88*gain*t.b/r.b).linear_to_srgb()
        var error=max(abs(observed.r-expected.r),abs(observed.g-expected.g),abs(observed.b-expected.b));maximum=max(maximum,error)
        print("SKIN_PROBE gain=",gain," expected=",expected," observed=",observed," error=",error)
    var st=SurfaceTool.new();st.begin(Mesh.PRIMITIVE_TRIANGLES)
    var colour=Color8(102,153,204)
    for p in [Vector3(-1,-1,0),Vector3(1,-1,0),Vector3(-1,1,0),Vector3(-1,1,0),Vector3(1,-1,0),Vector3(1,1,0)]:
        st.set_color(colour);st.add_vertex(p)
    quad.mesh=st.commit();var far=ShaderMaterial.new();var far_shader=Shader.new()
    far_shader.code=far_code.replace("shader_type spatial;","shader_type spatial;render_mode unshaded,cull_disabled;")
    far.shader=far_shader;quad.material_override=far
    await frames(4);await RenderingServer.frame_post_draw
    var observed=viewport.get_texture().get_image().get_pixel(32,32)
    maximum=max(maximum,abs(observed.r-colour.r),abs(observed.g-colour.g),abs(observed.b-colour.b))
    if maximum>.008:push_error("Native skin/LOD colour mismatch: "+str(maximum));quit(1);return
    print("SKIN_COLOUR_RENDER_PASS three source exposure gains, actual colour texture/uniforms, LOD vertex colour; maximum channel error=",maximum)
    quit()
