shader_type spatial;
render_mode cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

uniform vec4 foliage_color_top : source_color = vec4(0.15, 0.42, 0.13, 1.0);
uniform vec4 foliage_color_bottom : source_color = vec4(0.07, 0.24, 0.08, 1.0);
uniform vec4 trunk_color : source_color = vec4(0.32, 0.18, 0.08, 1.0);
uniform float wind_strength : hint_range(0.0, 0.5) = 0.06;
uniform float wind_speed : hint_range(0.5, 5.0) = 1.8;

varying float v_height;
varying float v_color_var;

void vertex() {
	v_height = VERTEX.y;
	vec3 world_origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	v_color_var = fract(sin(dot(world_origin.xz, vec2(12.9898, 78.233))) * 43758.5453);

	float sway_factor = max(0.0, VERTEX.y - 0.22);
	float phase = TIME * wind_speed + world_origin.x * 1.7 + world_origin.z * 1.3;
	VERTEX.x += sin(phase) * sway_factor * wind_strength;
	VERTEX.z += cos(phase * 0.85) * sway_factor * wind_strength * 0.7;
}

void fragment() {
	if (v_height < 0.26) {
		ALBEDO = trunk_color.rgb;
		ROUGHNESS = 0.92;
		METALLIC = 0.0;
		SPECULAR = 0.1;
	} else {
		float h_norm = clamp((v_height - 0.26) / 0.85, 0.0, 1.0);
		vec3 leaf_col = mix(foliage_color_bottom.rgb, foliage_color_top.rgb, h_norm);
		leaf_col *= mix(0.88, 1.12, v_color_var);
		ALBEDO = leaf_col;
		ROUGHNESS = 0.85;
		METALLIC = 0.0;
		SPECULAR = 0.12;
	}
}
