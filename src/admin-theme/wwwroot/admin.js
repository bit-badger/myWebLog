const Admin = {
  /** The next index for a metadata item */
  nextMetaIndex : 0,

  /** The next index for a permalink */
  nextPermalink : 0,
  
  /**
   * Set the next meta item index
   * @param idx The index to set
   */
  setNextMetaIndex(idx) {
    this.nextMetaIndex = idx
  },

  /**
   * Set the next permalink index
   * @param idx The index to set
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
    nameField.name        = "metaNames"
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
    valueField.name        = "metaValues"
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
    linkField.name        = "prior"
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
   * Enable or disable podcast fields
   */
  toggleEpisodeFields() {
    const disabled = !document.getElementById("isEpisode").checked
    ;[ "media", "mediaType", "length", "duration", "subtitle", "imageUrl", "explicit", "chapterFile", "chapterType",
       "transcriptUrl", "transcriptType", "transcriptLang", "transcriptCaptions", "seasonNumber", "seasonDescription",
       "episodeNumber", "episodeDescription"
    ].forEach(it => document.getElementById(it).disabled = disabled)
  },
  
  /**
   * Check to enable or disable podcast fields
   */
  checkPodcast() {
    document.getElementById("podcastFields").disabled = !document.getElementById("isPodcast").checked
  },

  /**
   * Copy text to the clipboard
   * @param text {string} The text to be copied
   * @param elt {HTMLAnchorElement} The element on which the click was generated
   * @return {boolean} False, to prevent navigation
   */
  copyText(text, elt) {
    navigator.clipboard.writeText(text)
    elt.innerText = "Copied"
    return false
  },
  
  /**
   * Toggle the source of a custom RSS feed
   * @param source The source that was selected
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
   * @param idx The index of the metadata item to remove
   */
  removeMetaItem(idx) {
    document.getElementById(`meta_${idx}`).remove()
  },

  /**
   * Remove a permalink
   * @param idx The index of the permalink to remove
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
   * @param messages The messages from the response
   */
  showMessage(messages) {
    const msgs = messages.split(", ")
    msgs.forEach(msg => {
      const parts = msg.split("|||")
      if (parts.length < 2) return
      
      const msgDiv = document.createElement("div")
      msgDiv.className = `alert alert-${parts[0]} alert-dismissible fade show`
      msgDiv.setAttribute("role", "alert")
      msgDiv.innerHTML = parts[1]
      
      const closeBtn = document.createElement("button")
      closeBtn.type = "button"
      closeBtn.className = "btn-close"
      closeBtn.setAttribute("data-bs-dismiss", "alert")
      closeBtn.setAttribute("aria-label", "Close")
      msgDiv.appendChild(closeBtn)
      
      if (parts.length === 3) {
        msgDiv.innerHTML += `<hr>${parts[2]}`
      }
      document.getElementById("msgContainer").appendChild(msgDiv)
    })
  },

  /**
   * Set all "success" alerts to close after 4 seconds
   */
  dismissSuccesses() {
    [...document.querySelectorAll(".alert-success")].forEach(alert => {
      setTimeout(() => {
        (bootstrap.Alert.getInstance(alert) ?? new bootstrap.Alert(alert)).close()
      }, 4000)
    })
  }
}

htmx.on("htmx:afterOnLoad", function (evt) {
  const hdrs = evt.detail.xhr.getAllResponseHeaders()
  // Show messages if there were any in the response
  if (hdrs.indexOf("x-message") >= 0) {
    Admin.showMessage(evt.detail.xhr.getResponseHeader("x-message"))
    Admin.dismissSuccesses()
  }
})