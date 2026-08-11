shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix;

uniform vec4 chalk_color : source_color = vec4(0.95, 0.93, 0.86, 0.78);
uniform float chalk_strength : hint_range(0.0, 1.0) = 0.0;
uniform bool use_fill_progress = false;
uniform float fill_progress : hint_range(0.0, 1.0) = 1.0;

float chalk_hash(vec2 point) {
	return fract(sin(dot(point, vec2(127.1, 311.7))) * 43758.5453);
}

void fragment() {
	float broad_grain = chalk_hash(floor(UV * 310.0));
	float fine_grain = chalk_hash(floor(UV * 790.0) + vec2(19.0, 43.0));
	float powder = smoothstep(0.20, 0.86, broad_grain * 0.68 + fine_grain * 0.32);
	float coverage = mix(0.32, 1.0, powder);
	float progressive_mask = use_fill_progress ? step(UV.x, fill_progress) : 1.0;
	ALBEDO = chalk_color.rgb;
	ROUGHNESS = 1.0;
	ALPHA = chalk_color.a * chalk_strength * coverage * progressive_mask;
}
