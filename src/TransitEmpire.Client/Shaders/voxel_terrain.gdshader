shader_type spatial;
render_mode specular_schlick_ggx;

uniform float micro_grain_strength : hint_range(0.0, 0.25) = 0.065;
uniform float shore_wet_height : hint_range(-0.1, 0.25) = 0.08;
uniform float cliff_cavity_strength : hint_range(0.0, 0.5) = 0.16;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float hash2d(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(hash2d(i + vec2(0.0, 0.0)), hash2d(i + vec2(1.0, 0.0)), u.x),
		mix(hash2d(i + vec2(0.0, 1.0)), hash2d(i + vec2(1.0, 1.0)), u.x),
		u.y
	);
}

void fragment() {
	vec3 base_col = COLOR.rgb;
	float cam_dist = length(CAMERA_POSITION_WORLD - world_pos);
	float is_top = step(0.5, abs(world_normal.y));

	vec3 col = base_col;
	float n1 = 0.5;
	if (cam_dist < 85.0) {
		// Triplanar-like micro-grain in world space so top faces and cliff faces both get crisp voxel detail
		vec2 uv_top = world_pos.xz * 7.5;
		vec2 uv_side = (abs(world_normal.x) > 0.5 ? world_pos.zy : world_pos.xy) * vec2(6.0, 10.0);
		vec2 uv_sample = mix(uv_side, uv_top, is_top);
		n1 = value_noise(uv_sample);
		float n2 = cam_dist < 48.0 ? value_noise(uv_sample * 2.7) : 0.5;
		float dist_fade = 1.0 - smoothstep(55.0, 85.0, cam_dist);
		float grain = ((n1 * 0.65 + n2 * 0.35) - 0.5) * 2.0 * micro_grain_strength * dist_fade;
		col = clamp(base_col + vec3(grain), 0.0, 1.0);
	}

	// Subtle horizontal strata and base-of-step cavity shading on vertical voxel walls
	if (is_top < 0.5) {
		float step_frac = fract(world_pos.y * 5.0);
		float basal_ao = smoothstep(0.0, 0.35, step_frac);
		float strata = sin(world_pos.y * 38.0 + n1 * 2.0) * 0.03;
		col *= mix(1.0 - cliff_cavity_strength, 1.0, basal_ao);
		col = clamp(col + vec3(strata), 0.0, 1.0);
	}

	// Wet sandy/muddy shoreline transition near water level (water sits around y = -0.10 to -0.02)
	float shore_factor = 1.0 - smoothstep(-0.08, shore_wet_height, world_pos.y);
	vec3 wet_sand = mix(col * 0.72, vec3(0.46, 0.41, 0.31), 0.45);
	col = mix(col, wet_sand, shore_factor * 0.65);

	// High-altitude snow sparkle on bright alpine peaks
	float is_snow = smoothstep(0.78, 0.92, (base_col.r + base_col.g + base_col.b) / 3.0) * smoothstep(0.55, 0.90, world_pos.y);

	ALBEDO = col;
	ROUGHNESS = mix(0.86 - n1 * 0.08, 0.32, shore_factor * 0.75);
	ROUGHNESS = mix(ROUGHNESS, 0.45, is_snow * 0.5);
	SPECULAR = mix(0.18, 0.48, max(shore_factor, is_snow));
}

