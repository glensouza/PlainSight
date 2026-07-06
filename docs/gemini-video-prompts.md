# Gemini/Veo Video Animation Prompts

This guide provides tested prompt templates for animating static announcement images into seamless, silent 10-second cinemagraph-style video loops using Gemini/Veo image-to-video models.

These prompts replace older, under-specified instructions (e.g., "please create a 10 second animation"). Image-to-video models respond best to *positive descriptions of desired behavior*—what should move and how—rather than lists of negations. Each prompt below is structured to guide the model toward smooth, looping results while keeping text, logos, and faces completely still.

## Base Prompt

Use this as a starting point for any announcement image. Copy and paste the text verbatim into the Gemini/Veo prompt field (the app shows a **Copy** button for this exact text):

```
Animate this image into a seamless 10-second cinemagraph-style loop. Keep the camera completely locked — no pan, zoom, tilt, or rotation — and keep all text, logos, and faces perfectly static, sharp, and unchanged. Add only subtle ambient motion to the background and environment: drifting clouds, gently swaying foliage, soft moving light rays, floating dust particles, or shimmering water, whichever suits the scene. Keep the motion slow, smooth, and continuous, and make the final frame match the first so the clip loops seamlessly. Photorealistic, silent, no audio, no speech, no people moving, no new objects appearing, and no change to the overall style or composition.
```

## Scene-Specific Variants

### 1. Announcement Slide with Large Text

**Use this when** your image is a title slide, event poster, or graphic with large legible text (e.g., sermon title, service times, ministry announcement).

**Why:** These slides need the text to stay perfectly crisp and readable. The prompt emphasizes locking the camera and grounding text, then suggests motion only in subtle background elements that won't distract from reading.

```
Animate this announcement slide into a seamless 10-second loop. Keep the camera completely locked — no pan, zoom, tilt, rotation, or movement of any kind — so the text, title, and logos remain perfectly sharp, readable, and unchanged throughout. Add only very subtle ambient motion: a slow gradient shift in background color, gentle shimmer or glow around edges, soft floating particles, or a faint light sweep, whichever fits the overall design. Keep the motion minimal and smooth so viewers can easily read the text without distraction. The final frame must match the first frame to loop seamlessly. Silent, photorealistic, no audio, no speech, no faces, no objects appearing or moving.
```

### 2. Outdoor / Nature Photo

**Use this when** your image shows a landscape, building exterior, garden, outdoor event, or nature scene.

**Why:** Outdoor scenes have natural elements (sky, trees, water) that respond well to subtle animated effects. The prompt encourages slow cloud movement, foliage sway, and light diffusion without panning the camera.

```
Animate this outdoor scene into a seamless 10-second loop. Lock the camera completely — no pan, zoom, or tilt — so the landscape, buildings, and horizon remain perfectly stationary. Bring the scene to life with subtle natural motion: drifting clouds moving slowly across the sky, gentle swaying of trees and foliage in a light breeze, soft moving light rays filtering through branches, ripples or shimmer on any water surfaces, and maybe a faint atmospheric haze or mist. Keep all motion smooth, slow, and continuous. The final frame must match the first to loop seamlessly. Photorealistic, silent, no audio, no people moving, no new objects appearing, and no change to composition or style.
```

### 3. Worship / Abstract Background

**Use this when** your image is a gradient, abstract pattern, color field, light effect, or worship/spiritual aesthetic background (often used behind sermon text or as a standalone interstitial).

**Why:** Abstract and worship backgrounds can animate beautifully through color shifts, light movement, and particle effects without needing recognizable objects or camera movement. The prompt focuses on visual flow and mood.

```
Animate this abstract background into a seamless 10-second loop. Keep the entire frame perfectly locked — no zoom, pan, rotation, or camera movement — so the composition remains static. Enhance the mood with subtle flowing motion: slow, gentle shifts in lighting and color gradients, soft moving light rays or glows, floating particles or dust drifting through the scene, and smooth waves or ripples in any color fields. Keep the motion ethereal, continuous, and smooth. Make the final frame match the first so the loop is seamless. Silent, photorealistic or stylized as appropriate, no audio, no speech, no objects appearing, and no change to the overall aesthetic or style.
```

### 4. Portrait or Group Photo (People)

**Use this when** your image shows a person, group, or clergy member and you want to animate the environment while keeping everyone perfectly still.

**Why:** Faces and people must stay completely static (no lip movement, no head turns, no expression changes), but the background can subtly animate. This prompt is strict about the people staying still while allowing environmental motion.

```
Animate this portrait into a seamless 10-second loop while keeping all people, faces, and expressions completely frozen and unchanged. Lock the camera entirely — no zoom, pan, tilt, or rotation — so the people remain perfectly still and sharp. Add only subtle ambient motion in the background and environment: soft light diffusion, gentle color shifts in background gradients, floating dust or particles, swaying backgrounds, ripples if there is water, or light rays moving gently. All people must remain motionless and their faces must not move or change expression at all. The final frame must match the first to loop seamlessly. Photorealistic, silent, no audio, no speech, no facial movement, no lip sync, no new objects appearing, and no change to the overall composition or style.
```

### 5. Illustration / Graphic Art

**Use this when** your image is a hand-drawn, designed, or stylized graphic (not photorealistic), such as a painted illustration, vector art, or digital graphic.

**Why:** Illustrated and graphic artwork has a different aesthetic than photography. The prompt respects the original style and suggests animation that complements rather than contradicts the artistic approach.

```
Animate this illustrated graphic into a seamless 10-second loop while maintaining its original artistic style and aesthetic. Lock the camera completely — no pan, zoom, tilt, or rotation — so the composition and all artwork elements remain perfectly static. Add subtle motion that complements the graphic style: gentle color shifts or glows, soft particle drifts, subtle light effects, smooth ripples in any water or organic elements, or faint animation of background details, whichever fits the art style. Keep all motion smooth, slow, and continuous. The final frame must match the first to loop seamlessly. Maintain the illustrated or stylized appearance throughout, and preserve all text, logos, and foreground elements unchanged. Silent, no audio, no speech, no new objects appearing, and no change to the overall composition or artistic style.
```

## What to Avoid

- **Negation-only phrasing.** Avoid prompts like "don't pan, don't zoom, don't move faces." Instead, say what *should* move: "keep the camera locked" and "add subtle background motion."
- **Requesting camera moves on text-heavy slides.** Do not ask for pan, zoom, tilt, or rotation if the slide has prominent text; it will cause the text to blur or distort.
- **Asking for faces or lip movement.** Never request "person speaking," "smiling," "expression change," or "mouth moving." Faces must stay frozen.
- **Over-long rambling prompts.** If a prompt becomes longer than ~200 words, simplify or use a variant above instead.
- **Requesting style changes.** Don't ask the model to "make it more colorful," "add a different aesthetic," or "change the look." Keep the original style intact.

## Parameter Notes

- **Duration:** All prompts request exactly 10 seconds. Do not modify this duration in the prompt itself; set it via the app's **Duration** field instead.
- **Silent / No audio:** Every prompt includes "silent, no audio" to ensure the model does not generate background music, sound effects, or voiceover.
- **Aspect ratio:**
  - **Landscape (16:9):** Default for most church displays. Use the base prompt or variants as-is.
  - **Portrait (9:16):** If your image is portrait-oriented, add this clarification to the prompt: *"The video should be 9:16 portrait aspect ratio, matching the input image orientation."*
- **Watermark removal:** After the app generates the clip, you **must** run the watermark removal tool (found on the Content page as an action menu item or in the edit modal) to strip the Veo/Gemini watermark before adding the video to a playlist. The watermark appears as a subtle logo or text overlay and will distract from your announcement.

## Tips for Best Results

1. **Clear, well-lit source image:** Blurry or very dark images produce less stable animations.
2. **Test on a small group first:** Before adding an animated announcement to a service, play it on one or two screens to confirm it loops smoothly and the motion is what you expected.
3. **Use for background fills, not primary focal points:** Animated announcements work best as interstitials between other content or as background layers. Don't rely on them as the main content of a long service program.
4. **Reuse proven prompts:** If a particular variant (e.g., "Outdoor / Nature Photo") produces great results with your church's style, stick with it for similar images.
