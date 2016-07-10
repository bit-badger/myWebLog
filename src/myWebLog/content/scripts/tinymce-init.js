tinymce.init({
  menubar: false,
  plugins: [
    "advlist autolink link image lists charmap print preview hr anchor pagebreak spellchecker",
    "searchreplace wordcount visualblocks visualchars code fullscreen insertdatetime media nonbreaking",
    "save table contextmenu directionality emoticons template paste textcolor"
  ],
  selector: "textarea",
  toolbar: "styleselect | forecolor backcolor | bullist numlist | link unlink anchor | paste pastetext | spellchecker | visualblocks visualchars | code fullscreen"
})