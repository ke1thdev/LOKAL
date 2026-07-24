package handlers

import (
	"encoding/base64"
	"fmt"
	"net/http"
	"os"
	"path/filepath"
	"strconv"
	"time"
)

// UploadClassSlide stores a PNG snapshot of the current presentation slide
// and broadcasts it to the class, keeping students in sync with the presentation
// even when no activity is running.
func (h *Handler) UploadClassSlide(w http.ResponseWriter, r *http.Request) {
	classID, err := strconv.ParseInt(r.PathValue("id"), 10, 64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid class ID")
		return
	}

	var req struct {
		ImageBase64 string `json:"image_base64"`
	}
	if err := decodeJSON(r, &req); err != nil || req.ImageBase64 == "" {
		errResponse(w, http.StatusBadRequest, "image_base64 is required")
		return
	}

	data, err := base64.StdEncoding.DecodeString(req.ImageBase64)
	if err != nil {
		errResponse(w, http.StatusBadRequest, "invalid base64 image")
		return
	}

	dir := filepath.Join(h.UploadsDir, "slides")
	if err := os.MkdirAll(dir, 0755); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to store slide")
		return
	}

	// Save with a generic class name so it overwrites the previous slide
	path := filepath.Join(dir, fmt.Sprintf("class_%d_current.png", classID))
	if err := os.WriteFile(path, data, 0644); err != nil {
		errResponse(w, http.StatusInternalServerError, "failed to store slide")
		return
	}

	// Tell students the slide image has changed
	url := fmt.Sprintf("/uploads/slides/class_%d_current.png?t=%d", classID, time.Now().UnixMilli())

	// Try to get class code to broadcast to
	if class, cerr := h.DB.GetClassByID(classID); cerr == nil {
		h.Hub.BroadcastToRoom("class:"+class.Code, map[string]interface{}{
			"type": "slide_changed",
			"payload": map[string]interface{}{
				"class_id":  classID,
				"slide_url": fmt.Sprintf("/uploads/slides/class_%d_current.png", classID),
			},
		})
	}

	success(w, map[string]string{"message": "slide uploaded", "url": url})
}
