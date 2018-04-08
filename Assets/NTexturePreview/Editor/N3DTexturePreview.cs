﻿using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Vertx
{
	[CustomEditor(typeof(Texture3D))]
	[CanEditMultipleObjects]
	public class N3DTexturePreview : NTexturePreviewBase
	{
		/// <summary>
		/// An interface for overriding the Material used by this previewer
		/// </summary>
		public interface I3DMaterialOverride
		{
			/// <summary>
			/// Return a custom Material for N3DTexturePreview
			/// </summary>
			/// <returns>The Material used by the N3DTexturePreview if not null</returns>
			Material GetMaterial (Texture3D texture3D);
		}
		
		void OnEnable()
		{
			//When this inspector is created, also create the built-in inspector
			defaultEditor = CreateEditor(targets, Type.GetType("UnityEditor.Texture3DInspector, UnityEditor"));

			//Find all types of I3DMaterialOverride, and query whether there's a valid material for the current target.
			IEnumerable<Type> i3DMaterialOverrideTypes = (IEnumerable<Type>)Type.GetType("UnityEditor.EditorAssemblies, UnityEditor").GetMethod(
				"GetAllTypesWithInterface", BindingFlags.NonPublic | BindingFlags.Static, null, new[]{typeof(Type)}, null
				                            ).Invoke(null, new object[]{typeof(I3DMaterialOverride)});
			foreach (Type i3DMaterialOverrideType in i3DMaterialOverrideTypes)
			{
				I3DMaterialOverride i3DMaterialOverride = (I3DMaterialOverride) Activator.CreateInstance(i3DMaterialOverrideType);
				m_Material = i3DMaterialOverride.GetMaterial((Texture3D) target);
				if (m_Material != null)
					break;
			}
			
			rCallback = r => {
				material.SetFloat("_R", r ? 1 : 0);
			};
			gCallback = g => {
				material.SetFloat("_G", g ? 1 : 0);
			};
			bCallback = b => {
				material.SetFloat("_B", b ? 1 : 0);
			};
		}

		void OnDisable()
		{
			//When OnDisable is called, the default editor we created should be destroyed to avoid memory leakage.
			//Also, make sure to call any required methods like OnDisable
			MethodInfo disableMethod = defaultEditor.GetType().GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (disableMethod != null)
				disableMethod.Invoke(defaultEditor, null);
			DestroyImmediate(defaultEditor);
			
			if (m_PreviewUtility != null)
			{
				m_PreviewUtility.Cleanup();
				m_PreviewUtility = null;
			}
		}

		public override void OnInspectorGUI()
		{
			defaultEditor.OnInspectorGUI();
		}

		private float zoom = 3f;
		public override void OnPreviewSettings()
		{
			defaultEditor.OnPreviewSettings();
			bool hasR = false, hasG = false, hasB = false;
			// ReSharper disable once PossibleInvalidCastExceptionInForeachLoop
			foreach (Texture3D texture3D in targets)
			{
				if (texture3D == null) // texture might have disappeared while we're showing this in a preview popup
					continue;
				bool _hasR, _hasG, _hasB;
				NTexturePreview.CheckRGBFormats(texture3D.format, out _hasR, out _hasG, out _hasB);
				hasR = hasR || _hasR;
				hasB = hasB || _hasB;
				hasG = hasG || _hasG;
			}

			if (GUILayout.Button(s_Styles.scaleIcon, s_Styles.previewButton))
			{
				zoom = 3;
				Repaint();
			}
			
			DrawRGBToggles(hasR, hasB, hasG);
		}

		public Vector2 m_PreviewDir = new Vector2(0, 0);
		private PreviewRenderUtility    m_PreviewUtility;

		public override void OnPreviewGUI(Rect r, GUIStyle background)
		{
			if (!ShaderUtil.hardwareSupportsRectRenderTexture || !SystemInfo.supports3DTextures)
			{
				if (Event.current.type == EventType.Repaint)
					EditorGUI.DropShadowLabel(new Rect(r.x, r.y, r.width, 40), "3D texture preview not supported");
				return;
			}

			m_PreviewDir = Drag2D(m_PreviewDir, r);

			Event e = Event.current;
			
			if (e.type == EventType.ScrollWheel)
			{
				zoom = Mathf.Clamp(zoom + e.delta.y * 0.1f, 0.05f, 5f);
				m_PreviewUtility.camera.nearClipPlane = Mathf.Lerp(0.05f, 2, Mathf.InverseLerp(0.05f, 3f, zoom));
				e.Use();
				Repaint();
			}
			
			if (e.type != EventType.Repaint)
				return;
			
			InitPreview();
			material.mainTexture = target as Texture;

			m_PreviewUtility.BeginPreview(r, background);
			bool oldFog = RenderSettings.fog;
			Unsupported.SetRenderSettingsUseFogNoDirty(false);

			m_PreviewUtility.camera.transform.position = -Vector3.forward * zoom;
			
			m_PreviewUtility.camera.transform.rotation = Quaternion.identity;
			Quaternion rot = Quaternion.Euler(m_PreviewDir.y, 0, 0) * Quaternion.Euler(0, m_PreviewDir.x, 0);
			m_PreviewUtility.DrawMesh(mesh, Vector3.zero, rot, material, 0);
			m_PreviewUtility.Render();

			Unsupported.SetRenderSettingsUseFogNoDirty(oldFog);
			m_PreviewUtility.EndAndDrawPreview(r);
		}
		
		void InitPreview()
		{
			if (m_PreviewUtility == null)
			{
				m_PreviewUtility = new PreviewRenderUtility();
				m_PreviewUtility.camera.fieldOfView = 30.0f;
			}
		}

		private Material material {
			get {
				if (m_Material == null)
				{
					m_Material = EditorGUIUtility.LoadRequired("Previews/Preview3DTextureMaterial.mat") as Material;
				}
				return m_Material;
			}
		}
		private Material m_Material;
		
		#region PreviewGUI
		//This region contains code taken from the internal PreviewGUI class.
		private static readonly int sliderHash = "Slider".GetHashCode();
		public static Vector2 Drag2D(Vector2 scrollPosition, Rect position)
		{
			int controlId = GUIUtility.GetControlID(sliderHash, FocusType.Passive);
			Event current = Event.current;
			switch (current.GetTypeForControl(controlId))
			{
				case EventType.MouseDown:
					if (position.Contains(current.mousePosition) && (double) position.width > 50.0)
					{
						GUIUtility.hotControl = controlId;
						current.Use();
						EditorGUIUtility.SetWantsMouseJumping(1);
					}
					break;
				case EventType.MouseUp:
					if (GUIUtility.hotControl == controlId)
						GUIUtility.hotControl = 0;
					EditorGUIUtility.SetWantsMouseJumping(0);
					break;
				case EventType.MouseDrag:
					if (GUIUtility.hotControl == controlId)
					{
						scrollPosition -= current.delta * (!current.shift ? 1f : 3f) / Mathf.Min(position.width, position.height) * 140f;
						scrollPosition.y = Mathf.Clamp(scrollPosition.y, -90f, 90f);
						current.Use();
						GUI.changed = true;
					}
					break;
			}
			return scrollPosition;
		}
		#endregion

		private Mesh _mesh;

		protected Mesh mesh
		{
			get
			{
				if (_mesh == null)
					_mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
				return _mesh;
			}
		}

		public override bool HasPreviewGUI()
		{
			return defaultEditor.HasPreviewGUI();
		}
	}
}