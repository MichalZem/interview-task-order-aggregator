# Agregátor objednávek

Navrhněte webovou službu, která:

- Je napsaná v aktuální verzi .NET.
- Nabízí REST API endpoint pro přijetí jedné nebo více objednávek ve formátu:

```json
[
  {
    "productId": "456",
    "quantity": 5
  },
  {
    "productId": "789",
    "quantity": 42
  }
]
```

- Objednávky se pro další zpracování agregují - sčítají se počty kusů dle Id produktu.
- Agregované objednávky se ne častěji, než jednou za 20 vteřin, odešlou internímu systému - pro naše účely lze pouze naznačit a vypisovat JSON do konzole.
- Služba by měla počítat s možností velkého množství malých objednávek (stovky za vteřinu) pro relativně limitovaný počet Id produktů (stovky celkem).
- Způsob persistence dat by měl být rozšiřitelný a konfigurovatelný - pro naše účely bude stačit implementovat držení dat v paměti.
- Kód by měl obsahovat (alespoň nějaké) testy.
- Zkuste navrhnout další možná vylepšení a přímo je implementujte nebo jen naznačte / popište.
- Mějte kód takový, jako si představuje v produkční aplikaci.
- Kód odevzdejte nejlépe formou publikace na GitHub - možno i jako privátní repozitář.
