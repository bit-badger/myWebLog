/**
 * Support functions for the administrative UI
 */
this.Admin = {
  /**
   * The next index for a metadata item
   * @type {number}
   */
  nextMetaIndex : 0,

  /**
   * The next index for a permalink
   * @type {number}
   */
  nextPermalink : 0,
  
  /**
   * Set the next meta item index
   * @param {number} idx The index to set
   */
  setNextMetaIndex(idx) {
    this.nextMetaIndex = idx
  },

  /**
   * Set the next permalink index
   * @param {number} idx The index to set
   */
  setPermalinkIndex(idx) {
    this.nextPermalink = idx
  },

  /**
   * Create a metadata remove button
   * @returns {HTMLDivElement} The column with the remove button
   */
  createMetaRemoveColumn() {
    const removeBtn = document.createElement("button")
    removeBtn.type      = "button"
    removeBtn.className = "btn btn-sm btn-danger"
    removeBtn.innerHTML = "&minus;"
    removeBtn.setAttribute("onclick", `Admin.removeMetaItem(${this.nextMetaIndex})`)

    const removeCol = document.createElement("div")
    removeCol.className = "col-1 text-center align-self-center"
    removeCol.appendChild(removeBtn)
    
    return removeCol
  },
  
  /**
   * Create a metadata name field
   * @returns {HTMLInputElement} The name input element
   */
  createMetaNameField() {
    const nameField = document.createElement("input")
    
    nameField.type        = "text"
    nameField.name        = "MetaNames"
    nameField.id          = `metaNames_${this.nextMetaIndex}`
    nameField.className   = "form-control"
    nameField.placeholder = "Name"
    
    return nameField
  },

  /**
   * Create a metadata name column using the given name input field
   * @param {HTMLInputElement} field The name field for the column
   * @returns {HTMLDivElement} The name column
   */
  createMetaNameColumn(field) {
    const nameLabel = document.createElement("label")
    nameLabel.htmlFor   = field.id
    nameLabel.innerText = field.placeholder

    const nameFloat = document.createElement("div")
    nameFloat.className = "form-floating"
    nameFloat.appendChild(field)
    nameFloat.appendChild(nameLabel)

    const nameCol = document.createElement("div")
    nameCol.className = "col-3"
    nameCol.appendChild(nameFloat)
    
    return nameCol
  },

  /**
   * Create a metadata value field
   * @returns {HTMLInputElement} The metadata value field
   */
  createMetaValueField() {
    const valueField = document.createElement("input")
    
    valueField.type        = "text"
    valueField.name        = "MetaValues"
    valueField.id          = `metaValues_${this.nextMetaIndex}`
    valueField.className   = "form-control"
    valueField.placeholder = "Value"
    
    return valueField
  },

  /**
   * Create a metadata value column using the given input field
   * @param {HTMLInputElement} field The metadata value input field
   * @param {string|undefined} hintText Text to be added below the field
   * @returns {HTMLDivElement} The value column
   */
  createMetaValueColumn(field, hintText) {
    const valueLabel = document.createElement("label")
    valueLabel.htmlFor   = field.id
    valueLabel.innerText = field.placeholder

    const valueFloat = document.createElement("div")
    valueFloat.className = "form-floating"
    valueFloat.appendChild(field)
    valueFloat.appendChild(valueLabel)

    if (hintText) {
      const valueHint = document.createElement("div")
      valueHint.className = "form-text"
      valueHint.innerText = hintText
      valueFloat.appendChild(valueHint)
    }
    
    const valueCol = document.createElement("div")
    valueCol.className = "col-8"
    valueCol.appendChild(valueFloat)
    
    return valueCol
  },

  /**
   * Construct and add a metadata item row
   * @param {HTMLDivElement} removeCol The column with the remove button
   * @param {HTMLDivElement} nameCol The column with the name field
   * @param {HTMLDivElement} valueCol The column with the value field
   */
  createMetaRow(removeCol, nameCol, valueCol) {
    const newRow = document.createElement("div")
    newRow.className = "row mb-3"
    newRow.id        = `meta_${this.nextMetaIndex}`
    newRow.appendChild(removeCol)
    newRow.appendChild(nameCol)
    newRow.appendChild(valueCol)

    document.getElementById("metaItems").appendChild(newRow)
    this.nextMetaIndex++
  },
  
  /**
   * Add a new row for metadata entry
   */
  addMetaItem() {
    const nameField = this.createMetaNameField()
    
    this.createMetaRow(
      this.createMetaRemoveColumn(),
      this.createMetaNameColumn(nameField),
      this.createMetaValueColumn(this.createMetaValueField(), undefined))
    
    document.getElementById(nameField.id).focus()
  },

  /**
   * Add a new row for a permalink
   */
  addPermalink() {
    // Remove button
    const removeBtn = document.createElement("button")
    removeBtn.type      = "button"
    removeBtn.className = "btn btn-sm btn-danger"
    removeBtn.innerHTML = "&minus;"
    removeBtn.setAttribute("onclick", `Admin.removePermalink(${this.nextPermalink})`)

    const removeCol = document.createElement("div")
    removeCol.className = "col-1 text-center align-self-center"
    removeCol.appendChild(removeBtn)

    // Link
    const linkField = document.createElement("input")
    linkField.type        = "text"
    linkField.name        = "Prior"
    linkField.id          = `prior_${this.nextPermalink}`
    linkField.className   = "form-control"
    linkField.placeholder = "Link"

    const linkLabel = document.createElement("label")
    linkLabel.htmlFor   = linkField.id
    linkLabel.innerText = linkField.placeholder

    const linkFloat = document.createElement("div")
    linkFloat.className = "form-floating"
    linkFloat.appendChild(linkField)
    linkFloat.appendChild(linkLabel)

    const linkCol = document.createElement("div")
    linkCol.className = "col-11"
    linkCol.appendChild(linkFloat)

    // Put it all together
    const newRow = document.createElement("div")
    newRow.className = "row mb-3"
    newRow.id        = `meta_${this.nextPermalink}`
    newRow.appendChild(removeCol)
    newRow.appendChild(linkCol)

    document.getElementById("permalinks").appendChild(newRow)
    document.getElementById(linkField.id).focus()
    this.nextPermalink++
  },

  /**
   * Set the chapter type for a podcast episode
   * @param {"none"|"internal"|"external"} src The source for chapters for this episode
   */
  setChapterSource(src) {
    document.getElementById("containsWaypoints").disabled = src === "none"
    const isDisabled = src === "none" || src === "internal"
    const chapterFile = document.getElementById("chapterFile")
    chapterFile.disabled = isDisabled
    chapterFile.required = !isDisabled
    document.getElementById("chapterType").disabled = isDisabled
    const link = document.getElementById("chapterEditLink")
    if (link) link.style.display = src === "none" || src === "external" ? "none" : ""
  },
  
  /**
   * Enable or disable podcast fields
   */
  toggleEpisodeFields() {
    const disabled = !document.getElementById("isEpisode").checked
    let fields = [
      "media", "mediaType", "length", "duration", "subtitle", "imageUrl", "explicit", "transcriptUrl", "transcriptType",
      "transcriptLang", "transcriptCaptions", "seasonNumber", "seasonDescription", "episodeNumber", "episodeDescription"
    ]
    if (disabled) {
      fields.push("chapterFile", "chapterType", "containsWaypoints")
    } else {
      const src = [...document.getElementsByName("ChapterSource")].filter(it => it.checked)[0].value
      this.setChapterSource(src)
    }
    fields.forEach(it => document.getElementById(it).disabled = disabled)
  },
  
  /**
   * Check to enable or disable podcast fields
   */
  checkPodcast() {
    document.getElementById("podcastFields").disabled = !document.getElementById("isPodcast").checked
  },

  /**
   * Copy text to the clipboard
   * @param {string} text The text to be copied
   * @param {HTMLAnchorElement} elt The element on which the click was generated
   * @return {boolean} False, to prevent navigation
   */
  copyText(text, elt) {
    navigator.clipboard.writeText(text)
    elt.innerText = "Copied"
    return false
  },
  
  /**
   * Toggle the source of a custom RSS feed
   * @param {string} source The source that was selected
   */
  customFeedBy(source) {
    const categoryInput = document.getElementById("sourceValueCat")
    const tagInput      = document.getElementById("sourceValueTag")
    if (source === "category") {
      tagInput.value         = ""
      tagInput.disabled      = true
      categoryInput.disabled = false
    } else {
      categoryInput.selectedIndex = -1
      categoryInput.disabled      = true
      tagInput.disabled           = false
    }
  },
  
  /**
   * Remove a metadata item
   * @param {number} idx The index of the metadata item to remove
   */
  removeMetaItem(idx) {
    document.getElementById(`meta_${idx}`).remove()
  },

  /**
   * Remove a permalink
   * @param {number} idx The index of the permalink to remove
   */
  removePermalink(idx) {
    document.getElementById(`link_${idx}`).remove()
  },

  /**
   * Require transcript type if transcript URL is present
   */
  requireTranscriptType() {
    document.getElementById("transcriptType").required = document.getElementById("transcriptUrl").value.trim() !== ""
  },

  /**
   * Show messages that may have come with an htmx response
   * @param {string} messages The messages from the response
   */
  showMessage(messages) {
    const msgs = messages.split(", ")
    msgs.forEach(msg => {
      const parts = msg.split("|||")
      if (parts.length < 2) return
      
      // Create the toast header
      const toastType = document.createElement("strong")
      toastType.className = "me-auto text-uppercase"
      toastType.innerText = parts[0] === "danger" ? "error" : parts[0]
      
      const closeBtn = document.createElement("button")
      closeBtn.type = "button"
      closeBtn.className = "btn-close"
      closeBtn.setAttribute("data-bs-dismiss", "toast")
      closeBtn.setAttribute("aria-label", "Close")

      const toastHead = document.createElement("div")
      toastHead.className = `toast-header bg-${parts[0]}${parts[0] === "warning" ? "" : " text-white"}`
      toastHead.appendChild(toastType)
      toastHead.appendChild(closeBtn)

      // Create the toast body
      const toastBody = document.createElement("div")
      toastBody.className = `toast-body bg-${parts[0]} bg-opacity-25`
      toastBody.innerHTML = parts[1]
      if (parts.length === 3) {
        toastBody.innerHTML += `<hr>${parts[2]}`
      }
      
      // Assemble the toast
      const toast = document.createElement("div")
      toast.className = "toast"
      toast.setAttribute("role", "alert")
      toast.setAttribute("aria-live", "assertive")
      toast.setAttribute("aria-atomic", "true")
      toast.appendChild(toastHead)
      toast.appendChild(toastBody)

      document.getElementById("toasts").appendChild(toast)
      
      let options = { delay: 4000 }
      if (parts[0] !== "success") options.autohide = false
      
      const theToast = new bootstrap.Toast(toast, options)
      theToast.show()
    })
  },

  /**
   * Initialize any toasts that were pre-rendered from the server
   */
  showPreRenderedMessages() {
    [...document.querySelectorAll(".toast")].forEach(el => {
      if (el.getAttribute("data-mwl-shown") === "true" && el.className.indexOf("hide") >= 0) {
        document.removeChild(el)
      } else {
        const toast = new bootstrap.Toast(el,
            el.getAttribute("data-bs-autohide") === "false"
                ? { autohide: false } : { delay: 6000, autohide: true })
        toast.show()
        el.setAttribute("data-mwl-shown", "true")
      }
    })
  }
}

htmx.on("htmx:afterOnLoad", function (evt) {
  const hdrs = evt.detail.xhr.getAllResponseHeaders()
  // Initialize any toasts that were pre-rendered from the server
  Admin.showPreRenderedMessages()
  // Show messages if there were any in the response
  if (hdrs.indexOf("x-message") >= 0) {
    Admin.showMessage(evt.detail.xhr.getResponseHeader("x-message"))
  }
})

htmx.on("htmx:responseError", function (evt) {
  const xhr = evt.detail.xhr
  const hdrs = xhr.getAllResponseHeaders()
  // Show an error message if there were none in the response
  if (hdrs.indexOf("x-message") < 0) {
    Admin.showMessage(`danger|||${xhr.status}: ${xhr.statusText}`)
  }
})

document.addEventListener("DOMContentLoaded", Admin.showPreRenderedMessages, { once: true})
